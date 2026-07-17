// Minimal browser helpers for the shared RCL. Loaded by each host's index.html
// (web today, the MAUI WebView later, RM-02).
window.filerUi = {
    // Streams already-fetched bytes to the user as a file download: the API call
    // itself goes through the authenticated typed client (a plain <a href> could
    // not carry the bearer token), so only the save-dialog step needs the DOM.
    downloadFile: function (fileName, contentType, base64) {
        const bytes = Uint8Array.from(atob(base64), c => c.charCodeAt(0));
        const blob = new Blob([bytes], { type: contentType || "application/octet-stream" });
        const url = URL.createObjectURL(blob);
        try {
            const anchor = document.createElement("a");
            anchor.href = url;
            anchor.download = fileName || "document";
            document.body.appendChild(anchor);
            anchor.click();
            anchor.remove();
        } finally {
            URL.revokeObjectURL(url);
        }
    }
};
