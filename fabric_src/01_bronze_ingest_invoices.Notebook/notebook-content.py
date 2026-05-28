# Fabric notebook source

# METADATA ********************

# META {
# META   "kernel_info": {
# META     "name": "synapse_pyspark"
# META   },
# META   "dependencies": {
# META     "lakehouse": {
# META       "default_lakehouse": "4dbfc58a-822f-4b9e-ac82-7ca1dc04d3ff",
# META       "default_lakehouse_name": "DataStore",
# META       "default_lakehouse_workspace_id": "730b84d3-6698-4dd3-a726-f4095f07e59e",
# META       "known_lakehouses": [
# META         {
# META           "id": "4dbfc58a-822f-4b9e-ac82-7ca1dc04d3ff"
# META         }
# META       ]
# META     }
# META   }
# META }

# MARKDOWN ********************

# # 01 – Bronze: Ingest invoice run artifacts into Delta
# 
# **Source:** `Files/invoices/run-*/` (one folder per pipeline run, ~9 JSON/MD/PDF files each).
# 
# **Bronze outputs (Delta tables):**
# - `bronze_invoice_header` – one row per invoice (from `invoice.json`)
# - `bronze_invoice_lineitem` – one row per line item (exploded)
# - `bronze_ingestion_email` – email metadata + accept/reject reason (from `ingestion.json`)
# - `bronze_run_result` – end-to-end run outcome (from `result.json`)
# - `bronze_run_manifest` – file inventory + stage timestamps per run
# 
# All tables are partitioned/keyed by `run_id` (folder name) so re-runs are idempotent via `MERGE`.

# MARKDOWN ********************

# ## Setup

# CELL ********************

from pyspark.sql import functions as F, types as T
from notebookutils import mssparkutils

# Default lakehouse is attached via the notebook UI. Files/Tables resolve relatively.
INVOICES_ROOT = "Files/invoices"
BRONZE_SCHEMA = "bronze"
spark.sql(f"CREATE SCHEMA IF NOT EXISTS {BRONZE_SCHEMA}")

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# Discover run folders
runs = [f for f in mssparkutils.fs.ls(INVOICES_ROOT) if f.isDir]
run_ids = [r.name for r in runs]
print(f"Found {len(run_ids)} runs:")
for r in run_ids:
    print(" -", r)

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## Helper – read JSON files with `run_id` lineage

# CELL ********************

def read_json_per_run(filename: str):
    """Read <run>/filename across all runs; tag with run_id and source_path."""
    paths = [f"{INVOICES_ROOT}/{rid}/{filename}" for rid in run_ids]
    # multiline=true so pretty-printed JSON parses; mergeSchema for drift across vendors
    df = (spark.read
          .option("multiline", "true")
          .option("mergeSchema", "true")
          .json(paths))
    return (df
            .withColumn("source_path", F.input_file_name())
            .withColumn("run_id", F.regexp_extract("source_path", r"run-\d+", 0))
            .withColumn("ingested_at", F.current_timestamp()))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 1. `bronze_invoice_header` + `bronze_invoice_lineitem`

# CELL ********************

# Read invoice.json for runs where the file actually exists to avoid PATH_NOT_FOUND errors

# Re-discover run folders in case new runs were added after the earlier cell executed
runs = [f for f in mssparkutils.fs.ls(INVOICES_ROOT) if f.isDir]
run_ids = [r.name for r in runs]

# Filter to runs that contain invoice.json
existing_runs = []
for rid in run_ids:
    path = f"{INVOICES_ROOT}/{rid}/invoice.json"
    if mssparkutils.fs.exists(path):
        existing_runs.append(rid)

if not existing_runs:
    # No invoice.json files found – create an empty DataFrame with the expected schema
    header_schema = T.StructType([
        T.StructField("run_id", T.StringType()),
        T.StructField("source_path", T.StringType()),
        T.StructField("ingested_at", T.TimestampType()),
        T.StructField("fileName", T.StringType()),
        T.StructField("blobUrl", T.StringType()),
        T.StructField("invoiceId", T.StringType()),
        T.StructField("vendorName", T.StringType()),
        T.StructField("vendorEmail", T.StringType()),
        T.StructField("invoiceDate", T.StringType()),
        T.StructField("dueDate", T.StringType()),
        T.StructField("paymentTerms", T.StringType()),
        T.StructField("currency", T.StringType()),
        T.StructField("totalAmount", T.DoubleType()),
        T.StructField("documentType", T.StringType()),
        T.StructField("extractionStatus", T.StringType()),
        T.StructField("extractionNotes", T.StringType()),
        T.StructField("businessName", T.StringType()),
        T.StructField("fromDate", T.StringType()),
        T.StructField("toDate", T.StringType()),
        T.StructField("invoiceAmount", T.DoubleType()),
        T.StructField("invoiceCurrency", T.StringType()),
        T.StructField("exchangeRate", T.DoubleType()),
        T.StructField("convertedInvoiceAmount", T.DoubleType()),
        T.StructField("convertedInvoiceCurrency", T.StringType()),
    ])
    header = spark.createDataFrame([], header_schema)
else:
    # Use only the runs that actually have invoice.json
    paths = [f"{INVOICES_ROOT}/{rid}/invoice.json" for rid in existing_runs]
    raw_inv = (spark.read
               .option("multiline", "true")
               .option("mergeSchema", "true")
               .json(paths)
               .withColumn("source_path", F.input_file_name())
               .withColumn("run_id", F.regexp_extract("source_path", r"run-\d+", 0))
               .withColumn("ingested_at", F.current_timestamp()))

    # invoice.json shape: { "invoices": [ { ...header..., "lineItems": [...] } ] }
    inv = raw_inv.select("run_id", "source_path", "ingested_at", F.explode("invoices").alias("inv"))

    header_cols = [
        "fileName", "blobUrl", "invoiceId", "vendorName", "vendorEmail",
        "invoiceDate", "dueDate", "paymentTerms", "currency", "totalAmount",
        "documentType", "extractionStatus", "extractionNotes", "businessName",
        "fromDate", "toDate", "invoiceAmount", "invoiceCurrency",
        "exchangeRate", "convertedInvoiceAmount", "convertedInvoiceCurrency"
    ]

    header = inv.select(
        "run_id", "source_path", "ingested_at",
        *[F.col(f"inv.{c}").alias(c) for c in header_cols]
    )

(header.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
       .saveAsTable(f"{BRONZE_SCHEMA}.bronze_invoice_header"))

display(header.limit(20))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

lines = (inv
         .withColumn("line", F.explode("inv.lineItems"))
         .select(
             "run_id", "ingested_at",
             F.col("inv.invoiceId").alias("invoiceId"),
             F.col("inv.vendorName").alias("vendorName"),
             F.col("line.lineNo").alias("lineNo"),
             F.col("line.categoryName").alias("categoryName"),
             F.col("line.description").alias("description"),
             F.col("line.quantity").cast("double").alias("quantity"),
             F.col("line.unitPrice").cast("double").alias("unitPrice"),
             F.col("line.lineTotal").cast("double").alias("lineTotal"),
             F.col("line.convertedUnitPrice").cast("double").alias("convertedUnitPrice"),
             F.col("line.convertedLineTotal").cast("double").alias("convertedLineTotal"),
         ))
(lines.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
      .saveAsTable(f"{BRONZE_SCHEMA}.bronze_invoice_lineitem"))
display(lines.limit(20))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 2. `bronze_ingestion_email`

# CELL ********************

# Read ingestion.json only from runs where the file exists to avoid PATH_NOT_FOUND errors

# Re-discover run folders in case new runs were added after earlier cells executed
runs = [f for f in mssparkutils.fs.ls(INVOICES_ROOT) if f.isDir]
run_ids = [r.name for r in runs]

# Filter to runs that contain ingestion.json
existing_runs_ing = []
for rid in run_ids:
    path = f"{INVOICES_ROOT}/{rid}/ingestion.json"
    if mssparkutils.fs.exists(path):
        existing_runs_ing.append(rid)

if not existing_runs_ing:
    # No ingestion.json files found – create an empty DataFrame with the expected schema
    ing_schema = T.StructType([
        T.StructField("run_id", T.StringType()),
        T.StructField("ingested_at", T.TimestampType()),
        T.StructField("ingestionStatus", T.StringType()),
        T.StructField("reason", T.StringType()),
        T.StructField("email_id", T.StringType()),
        T.StructField("email_from", T.StringType()),
        T.StructField("email_from_name", T.StringType()),
        T.StructField("email_to", T.StringType()),
        T.StructField("email_subject", T.StringType()),
        T.StructField("email_date", T.DateType()),
        T.StructField("email_preview", T.StringType()),
        T.StructField("email_body", T.StringType()),
        T.StructField("attachment_count", T.IntegerType()),
    ])
    ing_flat = spark.createDataFrame([], ing_schema)
else:
    # Use only the runs that actually have ingestion.json
    paths = [f"{INVOICES_ROOT}/{rid}/ingestion.json" for rid in existing_runs_ing]
    ing = (spark.read
           .option("multiline", "true")
           .option("mergeSchema", "true")
           .json(paths)
           .withColumn("source_path", F.input_file_name())
           .withColumn("run_id", F.regexp_extract("source_path", r"run-\d+", 0))
           .withColumn("ingested_at", F.current_timestamp()))

    ing_flat = ing.select(
        "run_id", "ingested_at",
        F.col("ingestionStatus"),
        F.col("reason"),
        F.col("email.id").alias("email_id"),
        F.col("email.from").alias("email_from"),
        F.col("email.fromName").alias("email_from_name"),
        F.col("email.to").alias("email_to"),
        F.col("email.subject").alias("email_subject"),
        F.to_date(F.col("email.date")).alias("email_date"),
        F.col("email.preview").alias("email_preview"),
        F.col("email.body").alias("email_body"),
        F.size("email.attachments").alias("attachment_count"),
    )

(ing_flat.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
         .saveAsTable(f"{BRONZE_SCHEMA}.bronze_ingestion_email"))

display(ing_flat.limit(20))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 3. `bronze_run_result`

# CELL ********************

res = read_json_per_run("result.json")
# result.json schemas vary by run – keep as semi-structured JSON string for now
res_flat = res.select(
    "run_id", "ingested_at",
    F.to_json(F.struct([c for c in res.columns if c not in ("run_id", "source_path", "ingested_at")]))
     .alias("result_json")
)
(res_flat.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
         .saveAsTable(f"{BRONZE_SCHEMA}.bronze_run_result"))
display(res_flat.limit(20))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 4. `bronze_run_manifest` – file inventory & stage timestamps

# CELL ********************

rows = []
for rid in run_ids:
    for f in mssparkutils.fs.ls(f"{INVOICES_ROOT}/{rid}"):
        rows.append((rid, f.name, f.size, f.modifyTime))
manifest = spark.createDataFrame(rows, "run_id string, file_name string, size_bytes long, modify_time_ms long")
manifest = manifest.withColumn("modify_time", (F.col("modify_time_ms")/1000).cast("timestamp"))
(manifest.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
         .saveAsTable(f"{BRONZE_SCHEMA}.bronze_run_manifest"))
display(manifest.orderBy("run_id", "modify_time").limit(50))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## Sanity checks

# CELL ********************

for t in ["bronze_invoice_header", "bronze_invoice_lineitem", "bronze_ingestion_email", "bronze_run_result", "bronze_run_manifest"]:
    cnt = spark.table(f"{BRONZE_SCHEMA}.{t}").count()
    print(f"{t:30s} {cnt:>6} rows")

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }
