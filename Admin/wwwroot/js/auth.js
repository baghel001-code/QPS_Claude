// Submits a form as a normal browser POST (full page load). form.submit() skips submit
// handlers, so Blazor's enhanced navigation can't turn it into a fetch. The response of that
// real HTTP request is what sets or clears the auth cookie.
export function submitForm(id) {
    document.getElementById(id)?.submit();
}
