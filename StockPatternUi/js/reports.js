// Reports JavaScript file

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
          rows +=
            "<td>" + new Date(item.dateUpdated).toLocaleDateString() + "</td>";
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