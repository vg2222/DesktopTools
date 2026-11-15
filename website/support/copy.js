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
  });
  button.hidden = false;
});
