// Shared workflow helpers for invoice -> ledger pages.
// Loads mock JSON data and runs the matching logic used on the
// processing and exception screens.

async function loadJson(path) {
  const inPages = location.pathname.includes('/pages/');
  const prefix = inPages ? '../' : '';
  const res = await fetch(prefix + path);
  if (!res.ok) throw new Error(`Failed to load ${path}: HTTP ${res.status}`);
  return res.json();
}

async function loadWorkflowData() {
  const [invoices, ledger, rules, fxRates] = await Promise.all([
    loadJson('data/invoices.json'),
    loadJson('data/ledger.json'),
    loadJson('data/rules.json'),
    loadJson('data/fx-rates.json')
  ]);
  return { invoices: invoices.invoices, ledger, rules: rules.rules, fxRates: fxRates.rates };
}

function fmtMoney(amount, currency) {
  if (amount == null || isNaN(amount)) return '—';
  const c = currency || 'USD';
  return `${c} ${Number(amount).toFixed(2)}`;
}

function convertToAud(amount, fromCurrency, fxRates) {
  if (!fxRates || !fromCurrency) return null;
  const from = fromCurrency.toUpperCase();
  if (from === 'AUD') return amount;
  const rate = fxRates.find(r => r.from.toUpperCase() === from && r.to.toUpperCase() === 'AUD');
  if (!rate) return null;
  return Math.round(amount * rate.rate * 100) / 100;
}

// Locate a ledger item by description, scanning aliases too. Returns
// { category, item } or null.
function findLedgerItem(ledger, categoryName, description) {
  const cat = ledger.categories.find(c =>
    c.categoryName.toLowerCase() === (categoryName || '').toLowerCase());
  if (!cat) return { category: null, item: null };
  const desc = (description || '').toLowerCase();
  const item = cat.items.find(i =>
    i.itemName.toLowerCase() === desc ||
    (i.aliases || []).some(a => a.toLowerCase() === desc) ||
    desc.includes(i.itemName.toLowerCase()) ||
    (i.aliases || []).some(a => desc.includes(a.toLowerCase()))
  );
  return { category: cat, item: item || null };
}

// Decide an outcome for a single invoice line item using the rules.
// Returns one result row.
function evaluateLineItem(invoice, category, lineItem, ledger, rules) {
  const result = {
    invoiceId: invoice.invoiceId,
    vendorName: invoice.vendorName,
    categoryName: category.categoryName,
    lineItemId: lineItem.lineItemId,
    description: lineItem.description,
    quantity: lineItem.quantity,
    unitPrice: lineItem.unitPrice,
    lineTotal: lineItem.lineTotal,
    matchedLedgerCategory: null,
    matchedLedgerItem: null,
    status: 'exception',
    ruleApplied: null,
    reason: '',
    humanInLoop: false
  };

  const { category: ledgerCat, item: ledgerItem } = findLedgerItem(ledger, category.categoryName, lineItem.description);

  if (!ledgerCat) {
    const rule = rules.find(r => r.id === 'R6');
    result.ruleApplied = rule.id;
    result.reason = `Category "${category.categoryName}" is not in the approved ledger.`;
    return result;
  }
  result.matchedLedgerCategory = ledgerCat.categoryName;

  if (!ledgerItem) {
    const rule = rules.find(r => r.id === 'R5');
    result.ruleApplied = rule.id;
    result.reason = `No ledger sub-item matches "${lineItem.description}" under ${ledgerCat.categoryName}.`;
    return result;
  }
  result.matchedLedgerItem = ledgerItem.itemName;

  // High value short-circuit (R4) always routes to review.
  const highValueRule = rules.find(r => r.id === 'R4');
  if (highValueRule && lineItem.lineTotal > (highValueRule.thresholdAmount || 0)) {
    result.status = 'review';
    result.ruleApplied = highValueRule.id;
    result.humanInLoop = true;
    result.reason = `Line total ${fmtMoney(lineItem.lineTotal)} exceeds high-value threshold ${fmtMoney(highValueRule.thresholdAmount)}.`;
    return result;
  }

  // Dollar tolerance check (R2 / R3).
  const toleranceRule = rules.find(r => r.id === 'R2');
  const reviewRule = rules.find(r => r.id === 'R3');
  const expected = ledgerItem.expectedUnitPrice;
  const diff = Math.abs(lineItem.unitPrice - expected);
  const pctTolerance = expected * ((toleranceRule.tolerancePercent || 0) / 100);
  const absTolerance = toleranceRule.toleranceAbsolute || 0;
  const tolerance = Math.max(pctTolerance, absTolerance);

  if (diff <= tolerance) {
    result.status = 'matched';
    result.ruleApplied = (diff < 0.01) ? 'R1' : 'R2';
    result.reason = (diff < 0.01)
      ? `Exact match on category, item and unit price ${fmtMoney(expected)}.`
      : `Unit price ${fmtMoney(lineItem.unitPrice)} within ±${fmtMoney(tolerance)} of expected ${fmtMoney(expected)}.`;
    return result;
  }

  result.status = 'review';
  result.ruleApplied = reviewRule.id;
  result.humanInLoop = true;
  result.reason = `Unit price ${fmtMoney(lineItem.unitPrice)} differs from expected ${fmtMoney(expected)} by ${fmtMoney(diff)} (tolerance ${fmtMoney(tolerance)}).`;
  return result;
}

// Process a single invoice and return { summary, results, notes }.
function processInvoice(invoice, ledger, rules) {
  const results = [];
  for (const cat of invoice.categories) {
    for (const li of cat.lineItems) {
      results.push(evaluateLineItem(invoice, cat, li, ledger, rules));
    }
  }
  const summary = {
    matched: results.filter(r => r.status === 'matched').length,
    review: results.filter(r => r.status === 'review').length,
    exception: results.filter(r => r.status === 'exception').length,
    totalProcessed: results.length
  };
  const notes = buildNotes(invoice, summary, results);
  return { summary, results, notes };
}

function buildNotes(invoice, summary, results) {
  const parts = [];
  parts.push(`Processed ${summary.totalProcessed} line item(s) for invoice ${invoice.invoiceId} (${invoice.vendorName}).`);
  parts.push(`${summary.matched} matched, ${summary.review} need review, ${summary.exception} exception(s).`);
  const reviews = results.filter(r => r.status === 'review');
  if (reviews.length) {
    parts.push('Human-in-the-loop required for:');
    reviews.forEach(r => parts.push(`  • ${r.description} — ${r.reason}`));
  }
  const excs = results.filter(r => r.status === 'exception');
  if (excs.length) {
    parts.push('Sent to exception queue:');
    excs.forEach(r => parts.push(`  • ${r.description} — ${r.reason}`));
  }
  return parts.join('\n');
}

function statusBadge(status) {
  const map = {
    matched: ['#dcfce7', '#166534'],
    review: ['#fef3c7', '#92400e'],
    exception: ['#fee2e2', '#991b1b'],
    accepted: ['#dcfce7', '#166534'],
    rejected: ['#fee2e2', '#991b1b'],
    posted: ['#dbeafe', '#1e40af']
  };
  const [bg, fg] = map[status] || ['#e5e7eb', '#374151'];
  return `<span style="display:inline-block;padding:2px 10px;border-radius:999px;font-size:0.72rem;font-weight:600;background:${bg};color:${fg};">${esc(status)}</span>`;
}
