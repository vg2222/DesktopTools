document.querySelectorAll('.donation-card').forEach(card => {
  const address = card.querySelector('input');
  const button = card.querySelector('button');
  const status = card.querySelector('[role="status"]');
  const label = button.querySelector('[data-i18n]');
  button.addEventListener('click', async () => {
    status.textContent = '';
    let copied = false;
    try {
      await navigator.clipboard.writeText(address.value);
      copied = true;
    } catch {
      address.focus();
      address.select();
      try { copied = document.execCommand('copy'); } catch { /* Leave the address selected for manual copying. */ }
      if (copied) button.focus();
    }
    const key = copied ? 'addressCopied' : 'copyAddressManually';
    const copy = translations[document.documentElement.lang] || translations.en;
    status.dataset.i18n = key;
    status.textContent = copy[key];
    label.dataset.i18n = copied ? 'addressCopied' : 'copyAddress';
    label.textContent = copy[label.dataset.i18n];
    // The button itself confirms the copy, then returns to its normal label.
    if (copied && typeof setTimeout === 'function') {
      clearTimeout(button.resetTimer);
      button.resetTimer = setTimeout(() => {
        const current = translations[document.documentElement.lang] || translations.en;
        label.dataset.i18n = 'copyAddress';
        label.textContent = current.copyAddress;
        status.textContent = '';
        delete status.dataset.i18n;
      }, 2500);
    }
  });
  button.hidden = false;
});
