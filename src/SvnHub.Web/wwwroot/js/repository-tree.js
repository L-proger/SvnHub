(function () {
  const modalEl = document.getElementById('uploadModal');
  if (!modalEl) return;

  const modeInput = document.getElementById('uploadMode');
  const titleEl = document.getElementById('uploadModalTitle');
  const filesBlock = document.getElementById('uploadFilesBlock');
  const folderBlock = document.getElementById('uploadFolderBlock');
  const folderInput = document.getElementById('uploadFolder');
  const filesInput = document.getElementById('uploadFiles');
  const filesInfo = document.getElementById('uploadFilesInfo');
  const folderInfo = document.getElementById('uploadFolderInfo');

  const modal = new bootstrap.Modal(modalEl);

  function formatBytes(bytes) {
    if (!bytes || bytes < 0) bytes = 0;
    const units = ['B', 'KB', 'MB', 'GB', 'TB'];
    let size = bytes;
    let unit = 0;
    while (size >= 1024 && unit < units.length - 1) {
      size /= 1024;
      unit++;
    }
    if (unit === 0) return `${bytes} ${units[unit]}`;
    return `${size.toFixed(size < 10 ? 1 : 0)} ${units[unit]}`;
  }

  function updateInfo(inputEl, infoEl) {
    if (!inputEl || !infoEl) return;
    const list = inputEl.files;
    if (!list || list.length === 0) {
      infoEl.textContent = '';
      return;
    }
    let total = 0;
    for (let i = 0; i < list.length; i++) total += (list[i].size || 0);
    infoEl.textContent = `Selected ${list.length} file(s) - ${formatBytes(total)}`;
  }

  function setMode(mode) {
    modeInput.value = mode;
    if (mode === 'folder') {
      titleEl.textContent = 'Upload folder';
      filesBlock.classList.add('d-none');
      folderBlock.classList.remove('d-none');
      folderInput.disabled = false;
      filesInput.disabled = true;
      filesInput.value = '';
      if (filesInfo) filesInfo.textContent = '';
    } else {
      titleEl.textContent = 'Upload files';
      folderBlock.classList.add('d-none');
      filesBlock.classList.remove('d-none');
      filesInput.disabled = false;
      folderInput.disabled = true;
      folderInput.value = '';
      if (folderInfo) folderInfo.textContent = '';
    }
  }

  document.querySelectorAll('[data-upload-open]').forEach((btn) => {
    btn.addEventListener('click', () => {
      setMode(btn.getAttribute('data-upload-open') || 'files');
      modal.show();
    });
  });

  modalEl.addEventListener('hidden.bs.modal', () => {
    if (filesInput) filesInput.value = '';
    if (folderInput) folderInput.value = '';
    if (filesInfo) filesInfo.textContent = '';
    if (folderInfo) folderInfo.textContent = '';
  });

  if (filesInput) {
    filesInput.addEventListener('change', () => updateInfo(filesInput, filesInfo));
  }
  if (folderInput) {
    folderInput.addEventListener('change', () => updateInfo(folderInput, folderInfo));
  }
})();
