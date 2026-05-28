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

# # 04 – Pipeline observability
# 
# Measure how well the AI invoice-processing pipeline itself is performing.
# Sources: `bronze_run_manifest`, `bronze_ingestion_email`, `bronze_invoice_header`, `bronze_run_result`.
# 
# Outputs:
# - `obs_stage_latency` – per-run wall-clock per stage
# - `obs_extraction_quality` – success/failure counts by vendor
# - `obs_duplicate_runs` – invoices reprocessed multiple times

# CELL ********************

from pyspark.sql import functions as F, Window
BRONZE, OBS = "bronze", "obs"
spark.sql(f"CREATE SCHEMA IF NOT EXISTS {OBS}")

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## Stage latency – derive from file modify-times in the manifest
# 
# Stage mapping by filename suffix:
# - `.pdf` → `ingested`
# - `extract.md` / `extract.json` → `ocr`
# - `processing-agentinput.json` → `agent_input`
# - `processing-agentoutput.json` → `agent_output`
# - `processing.json` → `normalized`
# - `invoice.json` → `final`
# - `result.json` → `result`

# CELL ********************

m = spark.table(f"{BRONZE}.bronze_run_manifest")
m = m.withColumn("stage",
    F.when(F.col("file_name").endswith(".pdf"), "ingested")
     .when(F.col("file_name").endswith("extract.md"),    "ocr")
     .when(F.col("file_name").endswith("extract.json"),  "ocr")
     .when(F.col("file_name") == "processing-agentinput.json",  "agent_input")
     .when(F.col("file_name") == "processing-agentoutput.json", "agent_output")
     .when(F.col("file_name") == "processing.json", "normalized")
     .when(F.col("file_name") == "invoice.json",    "final")
     .when(F.col("file_name") == "result.json",     "result")
     .when(F.col("file_name") == "ingestion.json",  "ingestion")
     .otherwise("other"))

stage_ts = (m.filter("stage <> 'other'")
             .groupBy("run_id", "stage")
             .agg(F.max("modify_time").alias("stage_ts")))

pivot = stage_ts.groupBy("run_id").pivot("stage").agg(F.first("stage_ts"))
latency = pivot.select(
    "run_id",
    "ingested", "ocr", "agent_input", "agent_output", "normalized", "final", "result",
    (F.unix_timestamp("result") - F.unix_timestamp("ingested")).alias("total_seconds"),
    (F.unix_timestamp("ocr")    - F.unix_timestamp("ingested")).alias("sec_ingest_to_ocr"),
    (F.unix_timestamp("normalized") - F.unix_timestamp("agent_input")).alias("sec_agent_total"),
    (F.unix_timestamp("final") - F.unix_timestamp("normalized")).alias("sec_normalize_to_final"),
)
(latency.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
        .saveAsTable(f"{OBS}.obs_stage_latency"))
display(latency.orderBy("run_id"))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## Extraction quality by vendor

# CELL ********************

hdr = spark.table(f"{BRONZE}.bronze_invoice_header")
quality = (hdr.groupBy("vendorName")
    .agg(F.count("*").alias("runs"),
         F.sum(F.when(F.col("extractionStatus") == "ok", 1).otherwise(0)).alias("ok_runs"),
         F.sum(F.when(F.col("extractionStatus") != "ok", 1).otherwise(0)).alias("failed_runs"))
    .withColumn("success_rate_pct", F.round(100 * F.col("ok_runs") / F.col("runs"), 1)))
(quality.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
        .saveAsTable(f"{OBS}.obs_extraction_quality"))
display(quality)

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## Duplicate / reprocessed invoices

# CELL ********************

dupes = (hdr.groupBy("invoiceId", "vendorName")
    .agg(F.countDistinct("run_id").alias("run_count"),
         F.collect_set("run_id").alias("runs"))
    .filter("run_count > 1")
    .orderBy(F.col("run_count").desc()))
(dupes.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
      .saveAsTable(f"{OBS}.obs_duplicate_runs"))
display(dupes)

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## Ingestion accept / reject summary

# CELL ********************

ing = spark.table(f"{BRONZE}.bronze_ingestion_email")
display(ing.groupBy("ingestionStatus").agg(F.count("*").alias("runs")))
display(ing.select("run_id", "ingestionStatus", "reason", "email_from", "email_subject")
          .orderBy("run_id"))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }
