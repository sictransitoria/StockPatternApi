// Main JavaScript file for Stock Pattern API UI

function getAllSetups(baseURL) {
  const webMethod = baseURL + "/api/Stock/getAllExistingSetups";

  $.ajax({
    type: "GET",
    url: webMethod,
    contentType: "application/json; charset=utf-8",
    success: function (data) {
      let rows = "";
      $.each(data, function (index, item) {
        if (!item.isFinalized) {
          rows += "<tr class='stockSetupID_" + item.id + "'>";
          rows += "<td>" + item.ticker + "</td>";
          rows += "<td>" + new Date(item.date).toLocaleDateString() + "</td>";
          rows += "<td>" + item.close.toFixed(2) + "</td>";
          rows += "<td>" + item.high.toFixed(2) + "</td>";
          rows += "<td>" + item.low.toFixed(2) + "</td>";
          rows += "<td>" + item.volume.toLocaleString() + "</td>";
          rows += "<td>" + item.volMA.toLocaleString() + "</td>";
          rows += "<td>" + item.resistanceLevel.toFixed(2) + "</td>";
          rows += "<td>" + item.breakoutPrice.toFixed(2) + "</td>";
          rows += "<td>" + item.signal + "</td>";
          rows += "</tr>";
        }
      });
      $("#stockSetupTableBody").html(rows);

      const tbody = document.getElementById("stockSetupTableBody");
      const rowCount = tbody.rows.length;
      console.log("Total Setups: " + rowCount);
      document.getElementById("setupCount").innerText = rowCount;
    },
    error: function (xhr) {
      console.error("Processing Error:", xhr.status, xhr.responseText);
    },
  });
}

function getFinalResultsReport(baseURL) {
  const webMethod = baseURL + "/api/Stock/getFinalResultsReport";

  $.ajax({
    type: "GET",
    url: webMethod,
    contentType: "application/json; charset=utf-8",
    success: function (data) {
      let rows = "";
      $.each(data, function (index, item) {
        rows += "<tr>";
        rows += "<td>" + item.ticker + "</td>";
        rows += "<td>" + item.percentageDifference + "</td>";
        rows += "<td>" + item.greenOrRedDay + "</td>";
        rows += "<td>" + new Date(item.dateUpdated).toLocaleDateString() + "</td>";
        rows += "</tr>";
      });
      $("#finalResultsReportTableBody").html(rows);
    },
    error: function (xhr) {
      console.error("Processing Error:", xhr.status, xhr.responseText);
    },
  });
}

  function getAggregatedSummaryReport(baseURL) {
    const webMethod = baseURL + "/api/Stock/getAggregatedSummaryReport";

  $.ajax({
    type: "GET",
    url: webMethod,
    contentType: "application/json; charset=utf-8",
    success: function (data) {
      let rows = "";
      $.each(data, function (index, item) {
        rows += "<tr>";
        rows += "<td>" + item.totalTrades + "</td>";
        rows += "<td>" + item.greenCount + "</td>";
        rows += "<td>" + item.redCount + "</td>";
        rows += "<td>" + item.successRate.toFixed(2) + "%</td>";
        rows += "<td>" + item.avgReturnPct.toFixed(2) + "%</td>";
        rows += "<td>" + item.bestTradePct.toFixed(2) + "%</td>";
        rows += "<td>" + item.worstTradePct.toFixed(2) + "%</td>";
        rows += "</tr>";
      });
      $("#aggregatedSummaryReportTableBody").html(rows);
    },
    error: function (xhr) {
      console.error("Processing Error:", xhr.status, xhr.responseText);
    },
  });
}