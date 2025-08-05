// Main JavaScript file for Stock Pattern API UI

$(document).ready(function () {
  if (!$("#modalStockDetails").length) {
    console.error("Element with ID 'modalStockDetails' not found in DOM");
    return;
  }
  getAllSetups(baseURL);
  $(document).on("click", "#stockSetupTableBody tr", function () {
    const rowData = {
      ticker: $(this).children().eq(0).text(),
      date: $(this).children().eq(1).text(),
      stockSetupId: parseInt(
        $(this)
          .attr("class")
          .match(/stockSetupID_(\d+)/)[1]
      ),
    };
    $("#modalStockDetails").text(`Did you trade ticker ${rowData.ticker} on ${rowData.date}?`);
    $("#priceSoldAtInput").val("");
    $("#soldSelect").val("");
    $("#confirmModal").modal("show");
    $("#saveButton")
      .off("click")
      .on("click", function () {
        const sold = $("#soldSelect").val();
        if (!sold) {
          alert("Please select Yes or No.");
          return;
        }
        const dataToSave = {
          StockSetupId: rowData.stockSetupId,
          IsActive: sold === "yes" ? true : false,
        };
        if (sold === "yes") {
          const priceSoldAt = $("#priceSoldAtInput").val();
          if (!priceSoldAt || isNaN(priceSoldAt) || parseFloat(priceSoldAt) <= 0) {
            alert("Please enter a valid price greater than 0.");
            return;
          }
          dataToSave.PriceSoldAt = parseFloat(priceSoldAt).toFixed(2);
        }
        console.log("Data to save:", dataToSave);
        $.ajax({
          url: baseURL + "/api/Stock/saveToFinalResults",
          type: "POST",
          contentType: "application/json",
          data: JSON.stringify(dataToSave),
          success: function (response) {
            console.log("Server response:", response);
            $(".loader-container").removeClass("d-none");
            if (sold === "no") {
              alert(`Data Saved Successfully! You did not trade ticker ${rowData.ticker}.`);
            } 
            else {
              alert(`Data Saved Successfully! You sold ticker ${rowData.ticker} at ${dataToSave.PriceSoldAt}.`);
            }
            $("#confirmModal").modal("hide");
            setTimeout(() => {
              location.reload();
            }, 500);
          },
          error: function (xhr) {
            console.error("Error details:", xhr);
            if (xhr.status === 404) {
              alert("Stock setup not found.");
            } else if (xhr.status === 400) {
              alert("Invalid data: " + xhr.responseText);
            } else {
              alert("Error saving data: " + xhr.responseText);
            }
          },
          complete: function () {
            $(".loader-container").addClass("d-none");
          },
        });
      });
  });
  $("#soldSelect").on("change", function () {
    const priceInputContainer = $("#priceInputContainer");
    priceInputContainer.css("display", this.value === "yes" ? "block" : "none");
  });
  $("#getStockSetups").on("click", function () {
    $("#getStockSetupsLoader").show();
    $.ajax({
      url: baseURL + "/api/Stock/getStockSetups",
      type: "GET",
      success: function (response) {
        console.log("Stock setups retrieved:", response);
        getAllSetups(baseURL);
      },
      error: function (error) {
        if (error.status === 404) {
          alert("No setups found! Your algorithm might need some tuning.");
        }
        console.error("Error details:", error);
      },
      complete: function () {
        $("#getStockSetupsLoader").hide();
      },
    });
  });
});

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
      if (xhr.status === 404) {
        console.log("No setups found (404)");
        $("#setupCount").text("0");
        $("#stockSetupTableBody").html("");
      } else {
        console.error("Processing Error:", xhr.status, xhr.responseText);
      }
    },
  });
}
