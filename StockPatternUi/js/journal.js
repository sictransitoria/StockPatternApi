// Journals JavaScript file

$(document).ready(function () {
    getAllJournalEntries(baseURL);

    const textarea = document.getElementById('journalBody');
    const charCountDisplay = document.getElementById('charCount');

    if (textarea && charCountDisplay) {
        textarea.addEventListener('input', function () {
            const charCount = this.value.length;
            charCountDisplay.textContent = charCount;
            if (charCount >= 499) {
                this.value = this.value.substring(0, 499);
            }
        });
    } 
    else {
        console.error('Missing elements: journalBody or charCount');
    }
});

function getAllJournalEntries(baseURL) {
  const webMethod = baseURL + "/api/Stock/getAllJournalEntries";

  $("#getStockSetupsLoader").show();
  $.ajax({
    type: "GET",
    url: webMethod,
    contentType: "application/json; charset=utf-8",
    success: function (data) {
      const journalEntries = document.getElementById("journalEntries");
      journalEntries.innerHTML = "";
      $.each(data, function (index, item) {
        if (item.isActive) {
          const entryDiv = document.createElement("div");
          entryDiv.className = "card mb-2";
          entryDiv.innerHTML = `
            <div class="card-body">
              <h5 class="card-title">${item.entrySubject}</h5>
              <p class="card-text">${item.entryBody}</p>
              <small class="text-muted">Posted on ${new Date(item.date).toLocaleString()}</small>
            </div>
          `;
          journalEntries.insertBefore(entryDiv, journalEntries.firstChild);
        }
      });
    },
    error: function (xhr) {
      if (xhr.status === 404) {
        console.error("No journal entries found.");
      } 
      else {
        console.error("Processing Error:", xhr.status, xhr.responseText);
      }
    },
    complete: function () {
      $("#getStockSetupsLoader").hide();
    },
  });
}

function addJournalEntry() {
  const webMethod = baseURL + "/api/Stock/saveToJournalEntries";
  const entrySubject = document.getElementById("journalHeader");
  const entryBody = document.getElementById("journalBody");

  if (entryBody.value.trim() && entrySubject.value.trim()) {
    $("#getStockSetupsLoader").show();
    $.ajax({
      url: webMethod,
      type: "POST",
      contentType: "application/json",
      data: JSON.stringify({
        Date: new Date().toISOString(),
        EntrySubject: entrySubject.value,
        EntryBody: entryBody.value,
        IsActive: true,
      }),
      success: function (response) {
        const entryDiv = document.createElement("div");
        entryDiv.className = "card mb-2";
        entryDiv.innerHTML = `
          <div class="card-body">
            <h5 class="card-title">${response.entrySubject}</h5>
            <p class="card-text">${response.entryBody}</p>
            <small class="text-muted">Posted on ${new Date(response.date).toLocaleString()}</small>
          </div>
        `;
        const journalEntries = document.getElementById("journalEntries");
        journalEntries.insertBefore(entryDiv, journalEntries.firstChild);
        entrySubject.value = "";
        entryBody.value = "";
        entrySubject.style.border = "";
        entryBody.style.border = "";
      },
      error: function () {
        alert("Failed to save journal entry.");
      },
      complete: function () {
        $("#getStockSetupsLoader").hide();
      },
    });
  } 
  else {
    alert("Please fill out both the header and body.");
    if (!entrySubject.value.trim()) entrySubject.style.border = "2px solid red";
    if (!entryBody.value.trim()) entryBody.style.border = "2px solid red";
  }
}