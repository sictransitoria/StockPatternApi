// Journals JavaScript file

$(document).ready(function () {

    // Initialize variables
    let entriesPerPage = 5;
    let currentPage = 1;
    let allEntries = [];

    // Fetch and display initial journal entries
    getAllJournalEntries(baseURL);

    // Character count for textarea
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

    // Function to render journal entries based on pagination
    function renderEntries(entries, page, perPage) {
        const journalEntries = document.getElementById("journalEntries");
        journalEntries.innerHTML = "";
        const start = (page - 1) * perPage;
        const end = start + perPage;
        const paginatedEntries = entries.slice(0, end); // Show entries up to the current page limit

        paginatedEntries.forEach((item) => {
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
                journalEntries.appendChild(entryDiv); // Append to the end
            }
        });

        // Hide "Load More" button if all entries are displayed
        const loadMoreBtn = document.getElementById("loadMoreBtn");
        if (end >= entries.length) {
            loadMoreBtn.style.display = "none";
        } 
        else {
            loadMoreBtn.style.display = "block";
        }
    }

    // Function to fetch all journal entries
    function getAllJournalEntries(baseURL) {
        const webMethod = baseURL + "/api/Stock/getAllJournalEntries";

        $("#getStockSetupsLoader").show();
        $.ajax({
            type: "GET",
            url: webMethod,
            contentType: "application/json; charset=utf-8",
            success: function (data) {
                allEntries = data.sort((a, b) => new Date(b.date) - new Date(a.date)); // Sort by date, newest first
                renderEntries(allEntries, currentPage, entriesPerPage); // Render initial entries
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

    // Function to load more entries
    window.loadMoreEntries = function () {
        currentPage++; // Increment page
        renderEntries(allEntries, currentPage, entriesPerPage); // Re-render with updated page
    };

    // Function to add a new journal entry
    window.addJournalEntry = function () {
        const webMethod = baseURL + "/api/Stock/saveToJournalEntries";
        const entrySubject = document.getElementById("journalHeader");
        const entryBody = document.getElementById("journalBody");

        if (entrySubject.value.trim() && entryBody.value.trim()) {
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
                    // Add new entry to the beginning of allEntries
                    allEntries.unshift(response);
                    currentPage = 1; // Reset to first page to show new entry
                    renderEntries(allEntries, currentPage, entriesPerPage);
                    entrySubject.value = "";
                    entryBody.value = "";
                    entrySubject.style.border = "";
                    entryBody.style.border = "";
                    document.getElementById("charCount").textContent = "0";
                },
                error: function () {
                    alert("Failed to save journal entry.");
                },
                complete: function () {
                    $("#getStockSetupsLoader").hide();
                },
            });
        } else {
            alert("Please fill out both the header and body.");
            if (!entrySubject.value.trim()) entrySubject.style.border = "2px solid red";
            if (!entryBody.value.trim()) entryBody.style.border = "2px solid red";
        }
    };
});