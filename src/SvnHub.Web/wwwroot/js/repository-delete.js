(function () {
  const modalEl = document.getElementById('deleteCommitModal');
  const modalForm = document.getElementById('deleteCommitForm');
  const hiddenFields = document.getElementById('deleteCommitHiddenFields');
  const messageInput = document.getElementById('deleteCommitMessage');
  const targetEl = document.getElementById('deleteCommitTarget');

  if (!modalEl || !modalForm || !hiddenFields || !messageInput || !targetEl) return;

  const modal = new bootstrap.Modal(modalEl);

  function cloneHiddenFields(form) {
    hiddenFields.replaceChildren();

    form.querySelectorAll('input[type="hidden"]').forEach((input) => {
      if (input.name === '__RequestVerificationToken') return;

      const clone = input.cloneNode();
      hiddenFields.appendChild(clone);
    });
  }

  function getTarget(form) {
    const targetPath = form.querySelector('input[name="targetPath"]')?.value;
    const path = form.querySelector('input[name="path"]')?.value;
    return targetPath || path || '-';
  }

  document.addEventListener('submit', (event) => {
    const form = event.target;
    if (!(form instanceof HTMLFormElement)) return;
    if (form.getAttribute('data-delete-requires-message') !== 'true') return;
    if (form === modalForm) return;

    event.preventDefault();
    modalForm.action = form.action;
    modalForm.method = form.method || 'post';
    cloneHiddenFields(form);
    targetEl.textContent = getTarget(form);
    messageInput.value = '';
    modal.show();
  });

  modalEl.addEventListener('shown.bs.modal', () => {
    messageInput.focus();
  });
})();
