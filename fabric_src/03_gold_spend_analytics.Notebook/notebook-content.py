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

# # 03 – Gold: Spend analytics
# 
# Business-facing aggregates. All amounts shown in AUD (`convertedLineTotal` / `convertedInvoiceAmount`).
# 
# Outputs:
# - `gold_spend_by_vendor_month`
# - `gold_spend_by_category`
# - `gold_unit_price_benchmark`
# - `gold_payment_calendar`
# - `gold_fx_sensitivity`

# CELL ********************

from pyspark.sql import functions as F
SILVER, GOLD = "silver", "gold"
spark.sql(f"CREATE SCHEMA IF NOT EXISTS {GOLD}")
inv   = spark.table(f"{SILVER}.silver_invoice")
lines = spark.table(f"{SILVER}.silver_invoice_lineitem")

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 1. Spend by vendor × month

# CELL ********************

spend_vm = (inv
   .withColumn("invoice_month", F.date_format("invoiceDate", "yyyy-MM"))
   .groupBy("vendorName", "invoice_month")
   .agg(F.countDistinct("invoiceId").alias("invoices"),
        F.round(F.sum("totalAmount"), 2).alias("total_native"),
        F.round(F.sum("convertedInvoiceAmount"), 2).alias("total_aud"))
   .orderBy("invoice_month", F.col("total_aud").desc()))
(spend_vm.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
         .saveAsTable(f"{GOLD}.gold_spend_by_vendor_month"))
display(spend_vm)

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 2. Category mix

# CELL ********************

from pyspark.sql import Window

spend_cat = (lines
   .groupBy("vendorName", "categoryName")
   .agg(F.round(F.sum("convertedLineTotal"), 2).alias("spend_aud"),
        F.countDistinct("invoiceId").alias("invoices"),
        F.count("*").alias("line_count")))

# Use the Window class from pyspark.sql, not from pyspark.sql.functions
window_spec = Window.partitionBy("vendorName")

windowed = spend_cat.withColumn(
    "share_pct",
    F.round(100 * F.col("spend_aud") / F.sum("spend_aud").over(window_spec), 2)
)

(windowed.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
         .saveAsTable(f"{GOLD}.gold_spend_by_category"))

display(windowed.orderBy("vendorName", F.col("spend_aud").desc()))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 3. Unit-price benchmarking across vendors

# CELL ********************

bench = (lines.groupBy("categoryName")
   .agg(F.expr("percentile_approx(convertedUnitPrice, 0.5)").alias("median_unit_price_aud"),
        F.min("convertedUnitPrice").alias("min_unit_price"),
        F.max("convertedUnitPrice").alias("max_unit_price"),
        F.countDistinct("vendorName").alias("vendors"),
        F.count("*").alias("line_count")))
outliers = (lines.alias("l").join(bench.alias("b"), "categoryName")
    .withColumn("vs_median_pct",
                F.round(100 * (F.col("l.convertedUnitPrice") - F.col("b.median_unit_price_aud")) /
                        F.col("b.median_unit_price_aud"), 1))
    .filter(F.col("b.vendors") > 1)
    .select("l.invoiceId", "l.vendorName", "categoryName", "l.description",
            "l.convertedUnitPrice", "b.median_unit_price_aud", "vs_median_pct"))
(outliers.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
         .saveAsTable(f"{GOLD}.gold_unit_price_benchmark"))
display(outliers.orderBy(F.col("vs_median_pct").desc()))

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 4. Payment calendar (cash-out forecast)

# CELL ********************

cal = (inv
   .withColumn("due_week", F.date_trunc("week", F.col("dueDate")))
   .groupBy("due_week")
   .agg(F.countDistinct("invoiceId").alias("invoices"),
        F.round(F.sum("convertedInvoiceAmount"), 2).alias("due_aud"))
   .orderBy("due_week"))
(cal.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
      .saveAsTable(f"{GOLD}.gold_payment_calendar"))
display(cal)

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }

# MARKDOWN ********************

# ## 5. FX sensitivity (±5% on non-AUD invoices)

# CELL ********************

fx = (inv.filter(F.col("currency") != F.lit("AUD"))
   .groupBy("currency")
   .agg(F.round(F.sum("convertedInvoiceAmount"), 2).alias("base_aud"),
        F.round(F.sum("convertedInvoiceAmount") * 1.05, 2).alias("plus_5pct"),
        F.round(F.sum("convertedInvoiceAmount") * 0.95, 2).alias("minus_5pct")))
(fx.write.format("delta").mode("overwrite").option("overwriteSchema", "true")
     .saveAsTable(f"{GOLD}.gold_fx_sensitivity"))
display(fx)

# METADATA ********************

# META {
# META   "language": "python",
# META   "language_group": "synapse_pyspark"
# META }
