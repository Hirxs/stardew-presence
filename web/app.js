document.addEventListener('DOMContentLoaded', () => {
  const currentOrigin = window.location.origin;
  const uploadApiUrl = `${currentOrigin}/api/upload`;

  // 1. Update config code block with current domain
  const configCodeBlock = document.getElementById('config-code-block');
  const configSnippet = `{\n  "EnableDynamicFarmerImage": true,\n  "CustomUploadUrl": "${uploadApiUrl}"\n}`;
  if (configCodeBlock) {
    configCodeBlock.textContent = configSnippet;
  }

  // 2. Copy Config Button
  const copyConfigBtn = document.getElementById('copy-config-btn');
  if (copyConfigBtn) {
    copyConfigBtn.addEventListener('click', () => {
      navigator.clipboard.writeText(configSnippet).then(() => {
        const originalHtml = copyConfigBtn.innerHTML;
        copyConfigBtn.innerHTML = '<span class="icon">✓</span> Copied!';
        setTimeout(() => {
          copyConfigBtn.innerHTML = originalHtml;
        }, 2000);
      });
    });
  }

  // 3. Health Check / Status Pill
  const statusPill = document.getElementById('status-pill');
  const statusText = document.getElementById('status-text');

  async function checkHealth() {
    try {
      const res = await fetch('/api/upload', { method: 'GET' });
      if (res.ok) {
        const data = await res.json().catch(() => ({}));
        if (data.bucketConfigured === false) {
          statusPill.className = 'status-pill warning';
          statusText.textContent = 'R2 Bucket Unbound';
          statusPill.title = "Worker is running, but R2 bucket 'BUCKET' needs to be bound in Cloudflare settings.";
        } else {
          statusPill.className = 'status-pill online';
          statusText.textContent = 'API Online';
        }
      } else {
        statusPill.className = 'status-pill warning';
        statusText.textContent = `HTTP ${res.status}`;
      }
    } catch {
      statusPill.className = 'status-pill';
      statusText.textContent = 'Ready (Static)';
    }
  }

  checkHealth();

  // 4. File Drop & Upload Logic
  const dropZone = document.getElementById('drop-zone');
  const fileInput = document.getElementById('file-input');
  const uploadResult = document.getElementById('upload-result');
  const previewImg = document.getElementById('preview-img');
  const resultUrlInput = document.getElementById('result-url-input');
  const copyUrlBtn = document.getElementById('copy-url-btn');
  const resultFilename = document.getElementById('result-filename');
  const resultSize = document.getElementById('result-size');
  const statusMsg = document.getElementById('upload-status-message');

  function showMessage(text, isError = false) {
    if (!statusMsg) return;
    statusMsg.textContent = text;
    statusMsg.className = `status-msg ${isError ? 'error' : 'info'}`;
    statusMsg.classList.remove('hidden');
  }

  function hideMessage() {
    if (statusMsg) statusMsg.classList.add('hidden');
  }

  if (dropZone && fileInput) {
    dropZone.addEventListener('click', () => fileInput.click());

    dropZone.addEventListener('dragover', (e) => {
      e.preventDefault();
      dropZone.classList.add('dragover');
    });

    ['dragleave', 'dragend'].forEach(type => {
      dropZone.addEventListener(type, () => dropZone.classList.remove('dragover'));
    });

    dropZone.addEventListener('drop', (e) => {
      e.preventDefault();
      dropZone.classList.remove('dragover');
      if (e.dataTransfer.files && e.dataTransfer.files.length > 0) {
        handleUpload(e.dataTransfer.files[0]);
      }
    });

    fileInput.addEventListener('change', () => {
      if (fileInput.files && fileInput.files.length > 0) {
        handleUpload(fileInput.files[0]);
      }
    });
  }

  async function handleUpload(file) {
    hideMessage();
    if (!file.type.startsWith('image/')) {
      showMessage('Please select an image file (PNG, JPG, WebP).', true);
      return;
    }

    if (file.size > 5 * 1024 * 1024) {
      showMessage('File size exceeds the 5 MB limit.', true);
      return;
    }

    showMessage('Uploading to Cloudflare R2...');

    const formData = new FormData();
    formData.append('file', file, file.name);

    try {
      const response = await fetch('/api/upload', {
        method: 'POST',
        body: formData
      });

      const data = await response.json();

      if (!response.ok || !data.url) {
        throw new Error(data.error || `Upload failed with status ${response.status}`);
      }

      hideMessage();
      if (uploadResult) uploadResult.classList.remove('hidden');

      if (previewImg) previewImg.src = data.url;
      if (resultUrlInput) resultUrlInput.value = data.url;
      if (resultFilename) resultFilename.textContent = data.filename || file.name;
      if (resultSize) resultSize.textContent = `${(file.size / 1024).toFixed(1)} KB`;

    } catch (err) {
      showMessage(err.message || 'Error uploading file.', true);
    }
  }

  // 5. Copy URL Button
  if (copyUrlBtn && resultUrlInput) {
    copyUrlBtn.addEventListener('click', () => {
      navigator.clipboard.writeText(resultUrlInput.value).then(() => {
        const orig = copyUrlBtn.textContent;
        copyUrlBtn.textContent = 'Copied!';
        setTimeout(() => {
          copyUrlBtn.textContent = orig;
        }, 2000);
      });
    });
  }
});
