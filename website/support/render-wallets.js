// The initial card also provides a readable English fallback when JS is unavailable.
{
  const list = document.getElementById('wallet-list');
  const template = list.querySelector('.donation-card');
  const cards = wallets.map((wallet, index) => {
    const card = template.cloneNode(true);
    const suffix = index === 0 ? '' : '-' + index;
    const heading = card.querySelector('[data-wallet-coin]');
    heading.textContent = wallet.coin;
    heading.id = 'donation-token' + suffix;
    card.setAttribute('aria-labelledby', heading.id);
    card.querySelector('[data-wallet-network]').textContent = wallet.network;
    card.querySelector('[data-wallet-coin-icon]').src = wallet.coinIcon || '../icons/color_24_regular.svg';
    card.querySelector('[data-wallet-network-icon]').src = wallet.networkIcon || '../icons/shield_checkmark_24_regular.svg';
    card.querySelector('[data-wallet-qr]').src = wallet.qr;
    const input = card.querySelector('input');
    input.id = 'donation-address' + suffix;
    input.value = wallet.address;
    card.querySelector('label').htmlFor = input.id;
    const note = card.querySelector('[data-i18n-wallet]');
    note.id = 'network-note' + suffix;
    note.dataset.coin = wallet.coin;
    note.dataset.network = wallet.network;
    note.textContent = 'Send only ' + wallet.coin + ' on the ' + wallet.network + ' network.';
    input.setAttribute('aria-describedby', note.id);
    card.querySelector('.donation-explorer').href = wallet.explorerUrl;
    card.querySelector('[role="status"]').id = 'copy-status' + suffix;
    const button = card.querySelector('button');
    button.id = 'copy-address' + suffix;
    button.setAttribute('aria-describedby', input.id + ' ' + note.id);
    return card;
  });
  list.replaceChildren(...cards);
}
