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

# # 02 – Silver: Curate & validate invoices
# 
# Reads Bronze and produces clean, deduped, DQ-checked Silver tables:
# - `silver_invoice` – one row per `invoiceId` (latest run wins)
# - `silver_invoice_lineitem` – line items for the latest run per invoice
# - `silver_dq_issues` – a row per failed data-quality check (long form)
# 
# **DQ rules implemented:**
# 1. `sum(lineTotal) == totalAmount` (± $0.01 tolerance)
# 2. `convertedLineTotal ≈ lineTotal * exchangeRate` (± 1%)
# 3. Required header fields non-null: `invoiceId, vendorName, invoiceDate, totalAmount, currency`
# 4. `extractionStatus == "ok"`
# 5. `dueDate >= invoiceDate`
# 6. No duplicate `invoiceId` after dedup (re-runs collapsed)

# CELL ********************

from pyspark.sql import functions as F, Window
BRONZE, SILVER = "bronze", "silver"
spark.sql(f"CREATE SCHEMA IF NOT EXISTS {SILVER}")

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## Dedup: keep latest run per `invoiceId`

# CELL ********************

hdr = spark.table(f"{BRONZE}.bronze_invoice_header")
w = Window.partitionBy("invoiceId").orderBy(F.col("run_id").desc())
hdr_latest = (hdr.withColumn("rn", F.row_number().over(w))
                 .filter("rn = 1")
                 .drop("rn"))

lines = spark.table(f"{BRONZE}.bronze_invoice_lineitem")
lines_latest = lines.join(
    hdr_latest.select("invoiceId", "run_id").withColumnRenamed("run_id", "keep_run"),
    ["invoiceId"]
).filter(F.col("run_id") == F.col("keep_run")).drop("keep_run")

print("Headers:", hdr.count(), "->", hdr_latest.count())
print("Lines  :", lines.count(), "->", lines_latest.count())

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## DQ checks

# CELL ********************

line_sums = (lines_latest.groupBy("invoiceId")
             .agg(F.round(F.sum("lineTotal"), 2).alias("sum_lineTotal"),
                  F.round(F.sum("convertedLineTotal"), 2).alias("sum_convertedLineTotal")))

checked = (hdr_latest.alias("h")
           .join(line_sums.alias("s"), "invoiceId", "left")
           .withColumn("reconcile_diff", F.round(F.col("sum_lineTotal") - F.col("totalAmount"), 2))
           .withColumn("fx_expected", F.round(F.col("totalAmount") * F.col("exchangeRate"), 2))
           .withColumn("fx_diff_pct",
                       F.abs(F.col("convertedInvoiceAmount") - F.col("fx_expected")) /
                       F.col("fx_expected")))

issues = []
issues.append(checked.filter(F.abs("reconcile_diff") > 0.01)
              .select("invoiceId", "vendorName", F.lit("line_total_mismatch").alias("rule"),
                      F.concat_ws(" ", F.lit("diff="), F.col("reconcile_diff")).alias("detail")))
issues.append(checked.filter(F.col("fx_diff_pct") > 0.01)
              .select("invoiceId", "vendorName", F.lit("fx_inconsistent").alias("rule"),
                      F.concat_ws(" ", F.lit("pct="), F.col("fx_diff_pct")).alias("detail")))
issues.append(checked.filter(F.col("extractionStatus") != F.lit("ok"))
              .select("invoiceId", "vendorName", F.lit("extraction_not_ok").alias("rule"),
                      F.col("extractionStatus").alias("detail")))
issues.append(checked.filter(F.col("dueDate") < F.col("invoiceDate"))
              .select("invoiceId", "vendorName", F.lit("due_before_invoice_date").alias("rule"),
                      F.concat_ws(" ", F.col("invoiceDate"), F.lit("->"), F.col("dueDate")).alias("detail")))
for col in ["invoiceId", "vendorName", "invoiceDate", "totalAmount", "currency"]:
    issues.append(checked.filter(F.col(col).isNull())
                  .select("invoiceId", "vendorName", F.lit(f"null_{col}").alias("rule"), F.lit(None).cast("string").alias("detail")))

dq = issues[0]
for i in issues[1:]:
    dq = dq.unionByName(i)
dq = dq.withColumn("detected_at", F.current_timestamp())
(dq.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
   .saveAsTable(f"{SILVER}.silver_dq_issues"))
display(dq)

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## Write Silver tables

# CELL ********************

(hdr_latest.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
           .saveAsTable(f"{SILVER}.silver_invoice"))
(lines_latest.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
            .saveAsTable(f"{SILVER}.silver_invoice_lineitem"))
print("✅ Silver written")

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# CELL ********************

# Quick summary
display(spark.sql(f"""
SELECT vendorName,
       COUNT(*)              AS invoices,
       ROUND(SUM(totalAmount),2)              AS total_native,
       ROUND(SUM(convertedInvoiceAmount),2)   AS total_aud
FROM {SILVER}.silver_invoice
GROUP BY vendorName
ORDER BY total_aud DESC
"""))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }
