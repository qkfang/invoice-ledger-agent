using InvLedgerAgent.Agents;
using InvLedgerAgent.Services;
using System.Text.Json;

namespace InvLedgerAgent.Api;

record NoticeUrlRequest(string Url);
record NoticeTextRequest(string Text);
record JsonRequest(string Json, string? RunName = null);
record ApproveRequest(string RunId, bool Approved);
record SendEmailRequest(string To, string Subject, string Body);
record CorrespondenceChatRequest(string PreviousResponseId, string Message);
record ProcessEmailRequest(string EmailJson, string AttachmentJson);
record ProcessScenarioRequest(string ScenarioName);
record RunFromStorageRequest(string RunName);

public static class Endpoints
{
    public static void MapAllEndpoints(this WebApplication app,
        InvLdgAgNotification notificationAgent,
        InvLdgAgCorrespondence correspondenceAgent,
        InvLdgAgExtractDI extractDiAgent, InvLdgAgExtractCU extractCuAgent,
        InvLdgAgIngestion ingestionAgent, InvLdgAgInvoice invoiceAgent,
        InvLdgAgProcessing processingAgent, InvLdgAgException exceptionAgent,
        InvLdgAgLedger ledgerAgent,
        DocIntelligenceService docService, ContentUnderstandingService cuService,
        BlobStorageService blobStorage, LocalRunStorageService localRunStorage,
        NotificationService notificationService,
        PendingApprovalStore approvalStore, FxRateService fxRateService,
        FabricLakehouseService? fabricLakehouse,
        string webRootPath,
        ILogger logger)
    {
        app.MapGet("/agents/instructions", () =>
        {
            var agents = new BaseAgent[]
            {
                ingestionAgent, invoiceAgent, processingAgent, exceptionAgent, ledgerAgent,
                extractDiAgent, extractCuAgent, notificationAgent, correspondenceAgent
            };
            return Results.Ok(agents.ToDictionary(a => a.AgentId, a => a.Instructions));
        });

        app.MapPost("/extract/di/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Extract DI upload: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var result = await docService.AnalyzeFromUrlAsync(blobUrl);
            return Results.Ok(new { markdown = result.Markdown, json = result.Json });
        });

        app.MapPost("/extract/cu/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            var fieldsJson = form["fields"].ToString();
            List<CuFieldSpec> fieldSpecs = new();
            if (!string.IsNullOrWhiteSpace(fieldsJson))
            {
                try
                {
                    fieldSpecs = JsonSerializer.Deserialize<List<CuFieldSpec>>(fieldsJson,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
                }
                catch (JsonException ex)
                {
                    return Results.BadRequest(new { error = $"invalid fields JSON: {ex.Message}" });
                }
            }

            logger.LogInformation("Extract CU upload: {FileName} ({Size} bytes), {FieldCount} custom field(s)",
                Sanitize(file.FileName), file.Length, fieldSpecs.Count);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);

            if (fieldSpecs.Count == 0)
            {
                var result = await cuService.AnalyzeFromUrlAsync(blobUrl);
                return Results.Ok(new { markdown = result.Markdown, json = result.Json, fields = new Dictionary<string, CuFieldValue>() });
            }

            var extraction = await cuService.AnalyzeWithCustomFieldsAsync(blobUrl, fieldSpecs);
            return Results.Ok(new { markdown = extraction.Markdown, json = extraction.Json, fields = extraction.Fields });
        });

        app.MapPost("/extract/agent/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Extract Agent upload: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var response = await extractDiAgent.RunAsync(blobUrl.ToString());
            return Results.Ok(new { response });
        });

        app.MapPost("/notification/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Notification upload: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var cuResult = await cuService.AnalyzeFromUrlAsync(blobUrl);
            var response = await extractCuAgent.RunAsync(cuResult.Markdown);
            return Results.Ok(new { markdown = cuResult.Markdown, json = cuResult.Json, response });
        });

        app.MapPost("/notification/assign", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Notification assign request ({Length} chars)", request.Json.Length);
            var response = await notificationAgent.RunAsync(request.Json);
            return Results.Ok(new { response });
        });

        app.MapPost("/correspondence/upload", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Correspondence upload: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var cuResult = await cuService.AnalyzeFromUrlAsync(blobUrl);
            var extracted = await extractCuAgent.RunAsync(cuResult.Markdown);

            var step = await correspondenceAgent.StartRunAsync(extracted);
            return BuildCorrespondenceResponse(step, approvalStore, extracted);
        });

        app.MapPost("/correspondence/chat", async (CorrespondenceChatRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.PreviousResponseId) || string.IsNullOrWhiteSpace(request.Message))
                return Results.BadRequest(new { error = "previousResponseId and message are required" });

            logger.LogInformation("Correspondence chat ({Length} chars)", request.Message.Length);
            var step = await correspondenceAgent.ChatAsync(request.PreviousResponseId, request.Message);
            return BuildCorrespondenceResponse(step, approvalStore, null);
        });

        app.MapPost("/correspondence/approve", async (ApproveRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.RunId))
                return Results.BadRequest(new { error = "runId is required" });

            var state = approvalStore.Get(request.RunId);
            if (state is null)
                return Results.NotFound(new { error = "run not found or already completed" });

            approvalStore.Remove(request.RunId);
            logger.LogInformation("Correspondence approval: runId={RunId} approved={Approved}", Sanitize(request.RunId), request.Approved);

            var step = await correspondenceAgent.ContinueRunAsync(state.PreviousResponseId, state.ApprovalItemId, request.Approved);
            return BuildCorrespondenceResponse(step, approvalStore, null);
        });

        app.MapPost("/ingestion/run", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Ingestion run request ({Length} chars)", request.Json.Length);
            var response = await ingestionAgent.RunAsync(
                $"Validate and extract the envelope from this vendor invoice document:\n\n{request.Json}");
            return Results.Ok(new { response });
        });

        app.MapPost("/invoice/run", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Invoice run request ({Length} chars)", request.Json.Length);
            var response = await invoiceAgent.RunAsync(
                $"Extract structured invoice details from this vendor invoice document:\n\n{request.Json}");
            return Results.Ok(new { response });
        });

        app.MapGet("/invoice/runs", async () =>
        {
            // Local temp folder is primary store for the invoice tab.
            var local = localRunStorage.ListRuns("run-");
            if (local.Count > 0)
                return Results.Ok(local.Take(20).ToArray());
            // Fallback to remote storage account for runs created elsewhere.
            var folders = await blobStorage.ListRunFoldersAsync("run-");
            return Results.Ok(folders.Take(20).ToArray());
        });

        app.MapGet("/invoice/runs/{runName}", async (string runName) =>
        {
            var safeRun = Path.GetFileName(runName);
            if (string.IsNullOrEmpty(safeRun)) return Results.BadRequest();

            // Prefer local temp folder; fall back to remote storage.
            List<string> files = localRunStorage.ListFiles(safeRun);
            var fromLocal = files.Count > 0;
            if (!fromLocal)
                files = await blobStorage.ListBlobsInFolderAsync(safeRun);
            if (files.Count == 0) return Results.NotFound();

            object? email = null;
            if (files.Contains("ingestion.json"))
            {
                string? json = null;
                if (fromLocal)
                {
                    var stream = localRunStorage.OpenRead(safeRun, "ingestion.json");
                    if (stream is not null)
                    {
                        using var reader = new StreamReader(stream);
                        json = await reader.ReadToEndAsync();
                    }
                }
                else
                {
                    var dl = await blobStorage.DownloadAsync($"{safeRun}/ingestion.json");
                    if (dl is not null)
                    {
                        using var reader = new StreamReader(dl.Value.Content);
                        json = await reader.ReadToEndAsync();
                    }
                }
                if (!string.IsNullOrEmpty(json))
                {
                    try { email = JsonSerializer.Deserialize<JsonElement>(json); } catch { email = null; }
                }
            }

            var pdfNames0 = files
                .Where(b => b.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var pdfs = new List<object>();
            foreach (var name in pdfNames0)
            {
                var baseName = Path.GetFileNameWithoutExtension(name);
                string? extractJson = null;
                string? extractMarkdown = null;
                if (fromLocal)
                {
                    if (localRunStorage.FileExists(safeRun, $"{baseName}.extract.json"))
                    {
                        var s = localRunStorage.OpenRead(safeRun, $"{baseName}.extract.json");
                        if (s is not null) { using var r = new StreamReader(s); extractJson = await r.ReadToEndAsync(); }
                    }
                    if (localRunStorage.FileExists(safeRun, $"{baseName}.extract.md"))
                    {
                        var s = localRunStorage.OpenRead(safeRun, $"{baseName}.extract.md");
                        if (s is not null) { using var r = new StreamReader(s); extractMarkdown = await r.ReadToEndAsync(); }
                    }
                }
                pdfs.Add(new
                {
                    name,
                    url = $"/ingestion/runs/{safeRun}/{Uri.EscapeDataString(name)}",
                    extractJson,
                    extractMarkdown
                });
            }

            return Results.Ok(new { runName = safeRun, email, pdfs });
        });

        app.MapPost("/invoice/run-from-storage", async (RunFromStorageRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.RunName))
                return Results.BadRequest(new { error = "runName is required" });

            var safeRun = Path.GetFileName(request.RunName);

            // Read from local temp folder first; fall back to remote storage.
            List<string> files = localRunStorage.ListFiles(safeRun);
            var fromLocal = files.Count > 0;
            if (!fromLocal)
                files = await blobStorage.ListBlobsInFolderAsync(safeRun);
            if (files.Count == 0) return Results.NotFound(new { error = "Run not found" });

            string? emailJson = null;
            if (files.Contains("ingestion.json"))
            {
                if (fromLocal)
                {
                    var stream = localRunStorage.OpenRead(safeRun, "ingestion.json");
                    if (stream is not null)
                    {
                        using var reader = new StreamReader(stream);
                        emailJson = await reader.ReadToEndAsync();
                    }
                }
                else
                {
                    var dl = await blobStorage.DownloadAsync($"{safeRun}/ingestion.json");
                    if (dl is not null)
                    {
                        using var reader = new StreamReader(dl.Value.Content);
                        emailJson = await reader.ReadToEndAsync();
                    }
                }
            }

            var account = blobStorage.AccountName;
            var pdfNames = files
                .Where(b => b.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var pdfList = new List<object>();
            foreach (var name in pdfNames)
            {
                var storageUrl = $"https://{account}.blob.core.windows.net/notices/{safeRun}/{name}";
                var proxyUrl = $"/ingestion/runs/{safeRun}/{Uri.EscapeDataString(name)}";

                string? extractJson = null;
                string? extractMarkdown = null;
                try
                {
                    byte[]? pdfBytes = null;
                    if (fromLocal)
                    {
                        var stream = localRunStorage.OpenRead(safeRun, name);
                        if (stream is not null)
                        {
                            using var ms = new MemoryStream();
                            await stream.CopyToAsync(ms);
                            pdfBytes = ms.ToArray();
                        }
                    }
                    else
                    {
                        var dl = await blobStorage.DownloadAsync($"{safeRun}/{name}");
                        if (dl is not null)
                        {
                            using var ms = new MemoryStream();
                            await dl.Value.Content.CopyToAsync(ms);
                            pdfBytes = ms.ToArray();
                        }
                    }
                    if (pdfBytes is not null)
                    {
                        var result = await docService.AnalyzeFromBytesAsync(BinaryData.FromBytes(pdfBytes));
                        extractJson = result.Json;
                        extractMarkdown = result.Markdown;
                        var baseName = Path.GetFileNameWithoutExtension(name);
                        await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, safeRun, $"{baseName}.extract.json", extractJson ?? string.Empty);
                        await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, safeRun, $"{baseName}.extract.md", extractMarkdown ?? string.Empty);
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "DI extract failed for {Pdf}", Sanitize(name));
                }

                pdfList.Add(new
                {
                    name,
                    storageUrl,
                    proxyUrl,
                    extractJson,
                    extractMarkdown
                });
            }

            var urlList = string.Join("\n", pdfNames.Select(n => $"- {n}: https://{account}.blob.core.windows.net/notices/{safeRun}/{n}"));
            var prompt = pdfNames.Count == 0
                ? $"Extract invoice details from this email (no PDF attachments):\n\n{emailJson ?? "(no email)"}"
                : $"Extract structured invoice details for each attached PDF. Use the extractDoc_DI tool against the storage URL of each PDF.\n\nEmail:\n{emailJson ?? "(no email)"}\n\nPDF Documents:\n{urlList}";

            logger.LogInformation("Invoice run-from-storage: {Run}, {Count} PDF(s)", Sanitize(safeRun), pdfNames.Count);
            var response = await invoiceAgent.RunAsync(prompt);

            object? emailObj = null;
            if (!string.IsNullOrEmpty(emailJson))
            {
                try { emailObj = JsonSerializer.Deserialize<JsonElement>(emailJson); } catch { emailObj = emailJson; }
            }

            // Persist parsed invoice JSON so processing/exception/ledger tabs can reuse it.
            var match = System.Text.RegularExpressions.Regex.Match(response ?? string.Empty, @"\{[\s\S]*\}");
            if (match.Success)
            {
                try
                {
                    using var doc = JsonDocument.Parse(match.Value);
                    var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                    await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, safeRun, "invoice.json", pretty);
                }
                catch (JsonException) { /* ignore parse failures */ }
            }

            return Results.Ok(new { runName = safeRun, response, email = emailObj, pdfs = pdfList });
        });

        app.MapGet("/invoice/runs/{runName}/invoice", async (string runName) =>
        {
            var safeRun = Path.GetFileName(runName);
            if (string.IsNullOrEmpty(safeRun)) return Results.BadRequest();
            var stream = localRunStorage.OpenRead(safeRun, "invoice.json");
            if (stream is null) return Results.NotFound(new { error = "Invoice not extracted yet. Run the invoice agent for this run first." });
            using var reader = new StreamReader(stream);
            var json = await reader.ReadToEndAsync();
            try
            {
                using var doc = JsonDocument.Parse(json);
                return Results.Ok(doc.RootElement.Clone());
            }
            catch (JsonException)
            {
                return Results.Problem("Cached invoice.json is not valid JSON.");
            }
        });

        app.MapPost("/invoice/process-doc", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            logger.LogInformation("Invoice process-doc: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var blobUrl = await blobStorage.UploadAsync(stream, file.FileName);
            var cuResult = await cuService.AnalyzeFromUrlAsync(blobUrl);
            var response = await invoiceAgent.RunAsync(
                $"Extract structured invoice details from this document. Use the extractDoc_CU tool with this document URL: {blobUrl}");
            return Results.Ok(new { markdown = cuResult.Markdown, json = cuResult.Json, response });
        });

        app.MapPost("/processing/run", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            // Provide the agent with the approved ledger and matching rules so it can classify line items.
            string ledgerJson = "{}", rulesJson = "[]";
            try { ledgerJson = await File.ReadAllTextAsync(Path.Combine(webRootPath, "data", "ledger.json")); } catch { }
            try
            {
                var rulesRaw = await File.ReadAllTextAsync(Path.Combine(webRootPath, "data", "rules.json"));
                using var rdoc = JsonDocument.Parse(rulesRaw);
                rulesJson = rdoc.RootElement.TryGetProperty("rules", out var rEl) ? rEl.GetRawText() : rulesRaw;
            }
            catch { }

            logger.LogInformation("Processing run request ({Length} chars), run={Run}", request.Json.Length, Sanitize(request.RunName ?? ""));
            var input = $"Process the following payload. Use fx_convert to convert each invoice totalAmount to AUD, then classify every line item against the ledger using the rules. Return JSON only.\n\n{{\n  \"invoices\": {request.Json},\n  \"ledger\": {ledgerJson},\n  \"rules\": {rulesJson}\n}}";
            var response = await processingAgent.RunAsync(input);

            // Persist the parsed agent JSON as processing.json so the exception page can load it.
            if (!string.IsNullOrWhiteSpace(request.RunName))
            {
                var safeRun = Path.GetFileName(request.RunName);
                var match = System.Text.RegularExpressions.Regex.Match(response ?? string.Empty, @"\{[\s\S]*\}");
                if (match.Success)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(match.Value);
                        var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
                        await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, safeRun, "processing.json", pretty);
                    }
                    catch (JsonException) { /* leave previous processing.json untouched on parse failure */ }
                }

                // Persist raw agent input/output so the processing page can reload them.
                var ioOpts = new JsonSerializerOptions { WriteIndented = true };
                await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, safeRun, "processing-agentinput.json",
                    JsonSerializer.Serialize(new { input }, ioOpts));
                await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, safeRun, "processing-agentoutput.json",
                    JsonSerializer.Serialize(new { output = response }, ioOpts));
            }

            return Results.Ok(new { input, response });
        });

        app.MapGet("/processing/runs/{runName}", async (string runName) =>
            await ReadCachedRunResultAsync(localRunStorage, runName, "processing.json"));

        app.MapGet("/processing/runs/{runName}/agentinput", async (string runName) =>
            await ReadCachedRunResultAsync(localRunStorage, runName, "processing-agentinput.json"));

        app.MapGet("/processing/runs/{runName}/agentoutput", async (string runName) =>
            await ReadCachedRunResultAsync(localRunStorage, runName, "processing-agentoutput.json"));

        app.MapPost("/exception/draft", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Exception draft request ({Length} chars)", request.Json.Length);
            var response = await exceptionAgent.RunAsync(request.Json);
            await CacheAgentResultAsync(localRunStorage, blobStorage, fabricLakehouse, logger, request.RunName, "exception.json", request.Json, response);
            return Results.Ok(new { response });
        });

        app.MapGet("/exception/runs/{runName}", async (string runName) =>
            await ReadCachedRunResultAsync(localRunStorage, runName, "exception.json"));

        app.MapPost("/ledger/ask", async (JsonRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.Json))
                return Results.BadRequest(new { error = "json is required" });

            logger.LogInformation("Ledger ask request ({Length} chars)", request.Json.Length);
            var response = await ledgerAgent.RunAsync(request.Json);
            return Results.Ok(new { response });
        });

        app.MapPost("/ingestion/save-to-fabric", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            if (fabricLakehouse is null)
                return Results.BadRequest(new { error = "Fabric Lakehouse is not configured. Set FABRIC_LAKEHOUSE_WORKSPACE_ID and FABRIC_LAKEHOUSE_ID." });

            logger.LogInformation("Save to Fabric: {FileName} ({Size} bytes)", Sanitize(file.FileName), file.Length);
            using var stream = file.OpenReadStream();
            var path = await fabricLakehouse.UploadAsync(stream, file.FileName, file.Length);
            return Results.Ok(new { path, documentName = file.FileName });
        });

        app.MapGet("/fx-rates", () => Results.Ok(fxRateService.GetRates()));

        app.MapPut("/fx-rates", (List<FxRate> rates) =>
        {
            fxRateService.UpdateRates(rates);
            return Results.Ok(fxRateService.GetRates());
        });

        app.MapPost("/ingestion/process-email", async (ProcessEmailRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.EmailJson))
                return Results.BadRequest(new { error = "emailJson is required" });

            var dt = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var folder = $"email-{dt}";
            var savedFiles = new List<object>();

            async Task<object> SaveJsonFile(string json, string fileName)
            {
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                var blobPath = $"{folder}/{fileName}";
                Uri blobUrl;
                using (var ms = new MemoryStream(bytes))
                    blobUrl = await blobStorage.UploadAsync(ms, blobPath);
                string? fabricPath = null;
                if (fabricLakehouse != null)
                {
                    using var ms = new MemoryStream(bytes);
                    fabricPath = await fabricLakehouse.UploadAsync(ms, blobPath, bytes.Length);
                }
                return new { name = fileName, folder, blobUrl = blobUrl.ToString(), fabricPath };
            }

            savedFiles.Add(await SaveJsonFile(request.EmailJson, $"{folder}-body.json"));
            if (!string.IsNullOrWhiteSpace(request.AttachmentJson))
                savedFiles.Add(await SaveJsonFile(request.AttachmentJson, $"{folder}-attachment.json"));

            var prompt = string.IsNullOrWhiteSpace(request.AttachmentJson)
                ? $"Process this email:\n\n{request.EmailJson}"
                : $"Process this email and its attachments:\n\n{request.EmailJson}\n\nAttachment data:\n{request.AttachmentJson}";

            logger.LogInformation("Process email: saved {Count} file(s) to folder {Folder}", savedFiles.Count, Sanitize(folder));
            var agentResponse = await ingestionAgent.RunAsync(prompt);
            return Results.Ok(new { folder, agentResponse, savedFiles });
        });

        app.MapGet("/ingestion/scenarios", async () =>
        {
            var scenariosPath = Path.Combine(webRootPath, "scenarios");
            if (!Directory.Exists(scenariosPath))
                return Results.Ok(Array.Empty<object>());

            var scenarios = new List<object>();
            foreach (var dir in Directory.GetDirectories(scenariosPath).OrderBy(d => d))
            {
                var name = Path.GetFileName(dir);
                var emailPath = Path.Combine(dir, "ingestion.json");
                if (!File.Exists(emailPath)) continue;

                var emailJson = await File.ReadAllTextAsync(emailPath);
                JsonElement emailObj;
                try { emailObj = JsonSerializer.Deserialize<JsonElement>(emailJson); }
                catch { continue; }

                var pdfs = Directory.GetFiles(dir, "*.pdf").Select(Path.GetFileName).ToList();
                scenarios.Add(new { name, email = emailObj, pdfs });
            }
            return Results.Ok(scenarios);
        });

        app.MapPost("/ingestion/process-scenario", async (ProcessScenarioRequest request) =>
        {
            if (string.IsNullOrWhiteSpace(request.ScenarioName))
                return Results.BadRequest(new { error = "scenarioName is required" });

            var scenarioDir = Path.Combine(webRootPath, "scenarios", request.ScenarioName);
            if (!Directory.Exists(scenarioDir))
                return Results.BadRequest(new { error = "Scenario not found" });

            var emailPath = Path.Combine(scenarioDir, "ingestion.json");
            if (!File.Exists(emailPath))
                return Results.BadRequest(new { error = "ingestion.json not found in scenario" });

            var emailJson = await File.ReadAllTextAsync(emailPath);
            JsonElement emailObj;
            try { emailObj = JsonSerializer.Deserialize<JsonElement>(emailJson); }
            catch { return Results.BadRequest(new { error = "Invalid ingestion.json" }); }

            var runDt = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var runName = $"run-{runDt}";
            localRunStorage.EnsureRunDir(runName);

            // Save ingestion.json to local temp, storage account, and Fabric Lakehouse.
            await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, runName, "ingestion.json", emailJson);

            var pdfFileNames = new List<string>();
            if (emailObj.TryGetProperty("attachments", out var attachments))
            {
                foreach (var att in attachments.EnumerateArray())
                {
                    if (att.TryGetProperty("name", out var nameEl))
                    {
                        var pdfName = nameEl.GetString() ?? "";
                        if (!pdfName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)) continue;
                        pdfFileNames.Add(pdfName);

                        var scenarioPdfPath = Path.Combine(scenarioDir, pdfName);
                        if (!File.Exists(scenarioPdfPath))
                        {
                            var sourcePath = Path.Combine(webRootPath, "invoices", pdfName);
                            if (File.Exists(sourcePath))
                                File.Copy(sourcePath, scenarioPdfPath);
                            else
                                logger.LogWarning("PDF {PdfName} not found in scenario or invoices folder", Sanitize(pdfName));
                        }
                    }
                }
            }

            var blobUrls = new Dictionary<string, string>();
            var savedFiles = new List<string> { "ingestion.json" };

            foreach (var pdfName in pdfFileNames)
            {
                var scenarioPdfPath = Path.Combine(scenarioDir, pdfName);
                if (!File.Exists(scenarioPdfPath)) continue;

                // Local temp first (primary).
                await localRunStorage.CopyFromAsync(runName, pdfName, scenarioPdfPath);
                savedFiles.Add(pdfName);

                // Then upload to remote storage (secondary).
                using (var stream = File.OpenRead(scenarioPdfPath))
                {
                    var blobUrl = await blobStorage.UploadAsync(stream, $"{runName}/{pdfName}");
                    blobUrls[pdfName] = blobUrl.ToString();
                }

                // Finally upload to Fabric Lakehouse (tertiary).
                if (fabricLakehouse is not null)
                {
                    try
                    {
                        var fi = new FileInfo(scenarioPdfPath);
                        using var fabricStream = File.OpenRead(scenarioPdfPath);
                        await fabricLakehouse.UploadAsync(fabricStream, $"{runName}/{pdfName}", fi.Length);
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Fabric upload failed for {File}", Sanitize(pdfName));
                    }
                }
            }

            var urlList = string.Join("\n", blobUrls.Select(kv => $"- {kv.Key}: {kv.Value}"));
            var prompt = string.IsNullOrEmpty(urlList)
                ? $"Process this email (no PDF attachments were found to extract):\n\n{emailJson}"
                : $"Process this email and extract invoice details from each attached PDF.\n\nEmail:\n{emailJson}\n\nPDF Documents (use extractDoc_DI tool for each URL):\n{urlList}";

            logger.LogInformation("Process scenario: {Scenario}, {Count} PDF(s)", Sanitize(request.ScenarioName), pdfFileNames.Count);
            var agentResponse = await ingestionAgent.RunAsync(prompt);

            var extractedFiles = new List<string>();
            var pdfs = new List<object>();
            foreach (var pdfName in blobUrls.Keys)
            {
                var baseName = Path.GetFileNameWithoutExtension(pdfName);
                var extractedFileName = $"{baseName}.extracted.json";
                var extractedContent = JsonSerializer.Serialize(new
                {
                    source = pdfName,
                    agentResponse,
                    processedAt = DateTime.UtcNow.ToString("o")
                }, new JsonSerializerOptions { WriteIndented = true });

                await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, runName, extractedFileName, extractedContent);
                extractedFiles.Add(extractedFileName);
                savedFiles.Add(extractedFileName);

                pdfs.Add(new
                {
                    name = pdfName,
                    url = $"/ingestion/runs/{runName}/{Uri.EscapeDataString(pdfName)}"
                });
            }

            var scenarioResult = new { runFolder = runName, agentResponse, extractedFiles, savedFiles, pdfs };
            await CacheScenarioResultAsync(localRunStorage, request.ScenarioName, scenarioResult);
            return Results.Ok(scenarioResult);
        });

        app.MapPost("/ingestion/upload-doc", async (HttpRequest http) =>
        {
            if (!http.HasFormContentType)
                return Results.BadRequest(new { error = "multipart/form-data required" });

            var form = await http.ReadFormAsync();
            var file = form.Files.FirstOrDefault();
            if (file is null || file.Length == 0)
                return Results.BadRequest(new { error = "file is required" });

            var safeFileName = Path.GetFileName(file.FileName);
            var ext = Path.GetExtension(safeFileName).ToLowerInvariant();
            var allowedExtensions = new HashSet<string> { ".pdf", ".png", ".jpg", ".jpeg" };
            if (!allowedExtensions.Contains(ext))
                return Results.BadRequest(new { error = "Only PDF and image files (png, jpg, jpeg) are allowed." });

            var emailBody = form["emailBody"].ToString();
            var runDt = DateTime.UtcNow.ToString("yyyyMMddHHmmss");
            var runName = $"run-{runDt}";
            localRunStorage.EnsureRunDir(runName);

            var savedFiles = new List<string>();

            // Save uploaded file to local run folder and blob storage.
            byte[] fileBytes;
            using (var ms = new MemoryStream())
            {
                await file.CopyToAsync(ms);
                fileBytes = ms.ToArray();
            }
            await localRunStorage.WriteAllBytesAsync(runName, safeFileName, fileBytes);
            savedFiles.Add(safeFileName);

            Uri blobUrl;
            using (var ms = new MemoryStream(fileBytes))
                blobUrl = await blobStorage.UploadAsync(ms, $"{runName}/{safeFileName}");

            if (fabricLakehouse is not null)
            {
                try
                {
                    using var ms = new MemoryStream(fileBytes);
                    await fabricLakehouse.UploadAsync(ms, $"{runName}/{safeFileName}", fileBytes.Length);
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Fabric upload failed for {File}", Sanitize(safeFileName));
                }
            }

            // Build and save ingestion.json.
            var ingestionObj = new
            {
                from = "noreply@localhost",
                fromName = "Manual Upload",
                to = "ap@inbox.local",
                subject = $"Uploaded invoice: {safeFileName}",
                date = DateTime.UtcNow.ToString("yyyy-MM-dd"),
                body = emailBody,
                attachments = new[] { new { name = safeFileName } }
            };
            var ingestionJson = JsonSerializer.Serialize(ingestionObj, new JsonSerializerOptions { WriteIndented = true });
            await MirrorRunFileAsync(localRunStorage, blobStorage, fabricLakehouse, logger, runName, "ingestion.json", ingestionJson);
            savedFiles.Add("ingestion.json");

            var prompt = $"Process this email and extract invoice details from the attached document.\n\nEmail:\n{ingestionJson}\n\nPDF Documents (use extractDoc_DI tool for each URL):\n- {safeFileName}: {blobUrl}";
            logger.LogInformation("Upload doc ingestion: {Run}, file={File}", Sanitize(runName), Sanitize(safeFileName));
            var agentResponse = await ingestionAgent.RunAsync(prompt);

            var pdfs = new List<object>();
            if (safeFileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                || safeFileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                || safeFileName.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                || safeFileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
            {
                pdfs.Add(new
                {
                    name = safeFileName,
                    url = $"/ingestion/runs/{runName}/{Uri.EscapeDataString(safeFileName)}"
                });
            }

            var result = new { runFolder = runName, agentResponse, savedFiles, pdfs };
            await CacheScenarioResultAsync(localRunStorage, runName, result);
            return Results.Ok(result);
        });

        app.MapGet("/ingestion/scenarios/{scenarioName}/result", (string scenarioName) =>
        {
            var safe = Path.GetFileName(scenarioName);
            if (string.IsNullOrEmpty(safe)) return Results.BadRequest();
            var path = Path.Combine(localRunStorage.RootPath, "scenario-cache", $"{safe}.json");
            if (!File.Exists(path)) return Results.NotFound();
            var json = File.ReadAllText(path);
            try
            {
                using var doc = JsonDocument.Parse(json);
                return Results.Ok(doc.RootElement.Clone());
            }
            catch (JsonException) { return Results.Problem("Cached scenario result is not valid JSON."); }
        });

        app.MapGet("/ingestion/runs/{runName}/{fileName}", async (string runName, string fileName) =>
        {
            var safeRun = Path.GetFileName(runName);
            var safeFile = Path.GetFileName(fileName);
            if (string.IsNullOrEmpty(safeRun) || string.IsNullOrEmpty(safeFile))
                return Results.BadRequest();

            // Local temp folder is the primary source.
            var localStream = localRunStorage.OpenRead(safeRun, safeFile);
            if (localStream is not null)
                return Results.Stream(localStream, LocalRunStorageService.ContentTypeFor(safeFile), enableRangeProcessing: true);

            // Fallback to remote storage.
            var result = await blobStorage.DownloadAsync($"{safeRun}/{safeFile}");
            if (result is null) return Results.NotFound();
            return Results.Stream(result.Value.Content, result.Value.ContentType, enableRangeProcessing: true);
        });

    }

    private static IResult BuildCorrespondenceResponse(AgentStepResult step, PendingApprovalStore approvalStore, string? extracted)
    {
        if (step.Pending is not null)
        {
            var runId = approvalStore.Add(step.Pending.ResponseId, step.Pending.ApprovalItemId, step.Pending.ServerLabel);
            return Results.Ok(new
            {
                status = "pending_approval",
                runId,
                previousResponseId = step.ResponseId,
                toolCall = new { serverLabel = step.Pending.ServerLabel },
                extracted
            });
        }

        return Results.Ok(new
        {
            status = "complete",
            response = step.Result,
            previousResponseId = step.ResponseId,
            extracted
        });
    }

    private static string Sanitize(string value) =>
        value.Replace('\r', ' ').Replace('\n', ' ');

    private static async Task MirrorRunFileAsync(LocalRunStorageService localRunStorage,
        BlobStorageService blobStorage, FabricLakehouseService? fabricLakehouse, ILogger logger,
        string runName, string fileName, string content)
    {
        var safeRun = Path.GetFileName(runName);
        if (string.IsNullOrEmpty(safeRun)) return;

        // Local temp first (primary).
        await localRunStorage.WriteAllTextAsync(safeRun, fileName, content);

        var bytes = System.Text.Encoding.UTF8.GetBytes(content);

        // Storage account (secondary).
        try
        {
            using var ms = new MemoryStream(bytes);
            await blobStorage.UploadAsync(ms, $"{safeRun}/{fileName}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Blob mirror failed for {Run}/{File}", Sanitize(safeRun), Sanitize(fileName));
        }

        // Fabric Lakehouse (tertiary).
        if (fabricLakehouse is not null)
        {
            try
            {
                using var ms = new MemoryStream(bytes);
                await fabricLakehouse.UploadAsync(ms, $"{safeRun}/{fileName}", bytes.Length);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Fabric mirror failed for {Run}/{File}", Sanitize(safeRun), Sanitize(fileName));
            }
        }
    }

    private static async Task CacheAgentResultAsync(LocalRunStorageService storage, string? runName, string fileName, string input, string? response)
    {
        if (string.IsNullOrWhiteSpace(runName)) return;
        var safeRun = Path.GetFileName(runName);
        if (string.IsNullOrEmpty(safeRun)) return;
        var payload = new { input, response, cachedAt = DateTime.UtcNow.ToString("o") };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await storage.WriteAllTextAsync(safeRun, fileName, json);
    }

    private static async Task CacheAgentResultAsync(LocalRunStorageService storage,
        BlobStorageService blobStorage, FabricLakehouseService? fabricLakehouse, ILogger logger,
        string? runName, string fileName, string input, string? response)
    {
        if (string.IsNullOrWhiteSpace(runName)) return;
        var payload = new { input, response, cachedAt = DateTime.UtcNow.ToString("o") };
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        await MirrorRunFileAsync(storage, blobStorage, fabricLakehouse, logger, runName, fileName, json);
    }

    private static async Task<IResult> ReadCachedRunResultAsync(LocalRunStorageService storage, string runName, string fileName)
    {
        var safeRun = Path.GetFileName(runName);
        if (string.IsNullOrEmpty(safeRun)) return Results.BadRequest();
        var stream = storage.OpenRead(safeRun, fileName);
        if (stream is null) return Results.NotFound();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();
        try
        {
            using var doc = JsonDocument.Parse(json);
            return Results.Ok(doc.RootElement.Clone());
        }
        catch (JsonException) { return Results.Problem($"Cached {fileName} is not valid JSON."); }
    }

    private static async Task CacheScenarioResultAsync(LocalRunStorageService storage, string scenarioName, object result)
    {
        var safe = Path.GetFileName(scenarioName);
        if (string.IsNullOrEmpty(safe)) return;
        var dir = Path.Combine(storage.RootPath, "scenario-cache");
        Directory.CreateDirectory(dir);
        var json = JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(Path.Combine(dir, $"{safe}.json"), json);
    }
}
