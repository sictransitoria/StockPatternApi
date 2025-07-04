function getAllSetups(baseURL) {
  const webMethod = baseURL + "/api/Stock/getAllExistingSetups";

  $.ajax({
    type: "GET",
    url: webMethod,
    contentType: "application/json; charset=utf-8",
    success: function (data) {
      let rows = "";
      $.each(data, function (index, item) {
        rows += "<tr class='stockSetupID_" + item.id + "'>";
        rows += "<td>" + item.ticker + "</td>";
        rows += "<td>" + new Date(item.date).toLocaleDateString() + "</td>";
        rows += "<td>" + item.close.toFixed(2) + "</td>";
        rows += "<td>" + item.high.toFixed(2) + "</td>";
        rows += "<td>" + item.low.toFixed(2) + "</td>";
        rows += "<td>" + item.volume.toLocaleString() + "</td>";
        rows += "<td>" + item.volMA.toLocaleString() + "</td>";
        rows += "</tr>";
      });
      $("#stockSetupTableBody").html(rows);
    },
    error: function (xhr) {
      console.error("Processing Error:", xhr.status, xhr.responseText);
    },
  });
}
