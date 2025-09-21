// Global JavaScript File

const baseURL = "https://localhost:7260"; // Visual Studio
// const baseURL = "http://localhost:5003"; // Visual Studio Code

// Get Current Year for Footer
const date = new Date().getFullYear();
document.getElementById("getCurrentYear").innerHTML = date;

// "Back To Top" Button
$(document).ready(function() {
    $(window).scroll(function() {
        if ($(this).scrollTop() > 100) {
            $('#backToTop').removeClass('d-none');
        } 
        else {
            $('#backToTop').addClass('d-none');
        }
    });

    $('#backToTop').click(function() {
        $('html, body').animate({ scrollTop: 0 }, 500);
        return false;
    });
});