{
  const address = document.getElementById("donation-address");
  const button = document.getElementById("copy-address");
  const status = document.getElementById("copy-status");

  button.addEventListener("click", async () => {
    status.textContent = "";
    let copied = false;
    try {
      await navigator.clipboard.writeText(address.value);
      copied = true;
    } catch {
      address.focus();
      address.select();
      try { copied = document.execCommand("copy"); } catch { /* Keep the address selected for manual copying. */ }
      if (copied) button.focus();
    }
    const key = copied ? "addressCopied" : "copyAddressManually";
    status.dataset.i18n = key;
    status.textContent = translations[document.documentElement.lang][key];
  });

  button.hidden = false;
}
