// The only JavaScript in the client.  Blazor can focus an element on its own
// (ElementReference.FocusAsync) but cannot select its text, and a spreadsheet in
// which typing appends to the old content instead of replacing it feels broken.
window.calcEngine = {
  focusAndSelect: function (element) {
    if (!element) {
      return;
    }
    element.focus();
    if (typeof element.select === 'function') {
      element.select();
    }
  }
};
