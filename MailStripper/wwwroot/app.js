// mailStripper - Client-side Interactive Engine

document.addEventListener('DOMContentLoaded', () => {
  // DOM Elements
  const dropZone = document.getElementById('dropZone');
  const fileInput = document.getElementById('fileInput');
  const tabUploadBtn = document.getElementById('tabUploadBtn');
  const tabPasteBtn = document.getElementById('tabPasteBtn');
  const panelUpload = document.getElementById('panelUpload');
  const panelPaste = document.getElementById('panelPaste');
  const pasteContent = document.getElementById('pasteContent');
  const charCount = document.getElementById('charCount');
  const submitPasteBtn = document.getElementById('submitPasteBtn');
  const clearPasteBtn = document.getElementById('clearPasteBtn');
  const loadSampleBtn = document.getElementById('loadSampleBtn');

  const ingestionSection = document.getElementById('ingestionSection');
  const loadingSection = document.getElementById('loadingSection');
  const loadingStatusText = document.getElementById('loadingStatusText');
  const resultsSection = document.getElementById('resultsSection');
  const stripAnotherBtn = document.getElementById('stripAnotherBtn');

  // Error Banner Elements
  const errorBanner = document.getElementById('errorBanner');
  const errorTitle = document.getElementById('errorTitle');
  const errorMessage = document.getElementById('errorMessage');
  const closeErrorBtn = document.getElementById('closeErrorBtn');

  // Results Elements
  const resSubjectPill = document.getElementById('resSubjectPill');
  const resEnginePill = document.getElementById('resEnginePill');
  const resOneSentenceTitle = document.getElementById('resOneSentenceTitle');
  const copyTitleBtn = document.getElementById('copyTitleBtn');
  const copySummaryBtn = document.getElementById('copySummaryBtn');
  const resBulletList = document.getElementById('resBulletList');
  const resShortSummaryText = document.getElementById('resShortSummaryText');
  const resFrom = document.getElementById('resFrom');
  const resTo = document.getElementById('resTo');
  const resDate = document.getElementById('resDate');
  const resRawSubject = document.getElementById('resRawSubject');
  const resWordCount = document.getElementById('resWordCount');
  const resAttachmentStats = document.getElementById('resAttachmentStats');
  const downloadAllZipBtn = document.getElementById('downloadAllZipBtn');
  const attachmentsGrid = document.getElementById('attachmentsGrid');
  const resBodyText = document.getElementById('resBodyText');
  const resBodyHtml = document.getElementById('resBodyHtml');
  const tabBodyText = document.getElementById('tabBodyText');
  const tabBodyHtml = document.getElementById('tabBodyHtml');
  const panelBodyText = document.getElementById('panelBodyText');
  const panelBodyHtml = document.getElementById('panelBodyHtml');

  // Modal Elements
  const previewModal = document.getElementById('previewModal');
  const modalFileName = document.getElementById('modalFileName');
  const modalDownloadBtn = document.getElementById('modalDownloadBtn');
  const modalBody = document.getElementById('modalBody');
  const closeModalBtn = document.getElementById('closeModalBtn');

  // Privacy Audit Modal Elements
  const privacyAuditBtn = document.getElementById('privacyAuditBtn');
  const privacyModal = document.getElementById('privacyModal');
  const closePrivacyModalBtn = document.getElementById('closePrivacyModalBtn');
  const runAuditBtn = document.getElementById('runAuditBtn');
  const auditExtConn = document.getElementById('auditExtConn');
  const auditStorage = document.getElementById('auditStorage');
  const auditCsp = document.getElementById('auditCsp');
  const auditTelemetry = document.getElementById('auditTelemetry');
  const auditFingerprint = document.getElementById('auditFingerprint');

  // Status Elements
  const mlStatusDot = document.getElementById('mlStatusDot');
  const mlStatusLabel = document.getElementById('mlStatusLabel');

  // Screen Reader Live Announcer
  const srLiveRegion = document.getElementById('srLiveRegion');
  let lastFocusedElement = null;

  function announceToScreenReader(message) {
    if (!srLiveRegion || !message) return;
    srLiveRegion.textContent = message;
  }

  let currentStrippedData = null;

  // Privacy Audit Modal Handlers
  if (privacyAuditBtn && privacyModal) {
    privacyAuditBtn.addEventListener('click', () => {
      lastFocusedElement = privacyAuditBtn;
      privacyModal.showModal();
      loadPrivacyAudit();
    });

    closePrivacyModalBtn.addEventListener('click', () => {
      privacyModal.close();
      if (lastFocusedElement) lastFocusedElement.focus();
    });

    privacyModal.addEventListener('click', (e) => {
      if (e.target === privacyModal) {
        privacyModal.close();
        if (lastFocusedElement) lastFocusedElement.focus();
      }
    });

    privacyModal.addEventListener('close', () => {
      if (lastFocusedElement) lastFocusedElement.focus();
    });

    runAuditBtn.addEventListener('click', loadPrivacyAudit);
  }

  async function loadPrivacyAudit() {
    auditFingerprint.textContent = 'Auditing active sockets...';
    try {
      const res = await fetch('/api/strip/privacy-audit');
      if (res.ok) {
        const audit = await res.json();
        auditExtConn.textContent = `${audit.externalConnectionsCount} (Air-Gapped: ${audit.isAirGapped ? 'Yes' : 'No'})`;
        auditStorage.textContent = audit.zeroDiskWritesVerified ? '100% Volatile RAM (0 disk files)' : audit.storagePolicy;
        auditCsp.textContent = "default-src 'self' (Enforced)";
        auditTelemetry.textContent = audit.telemetryStatus;
        auditFingerprint.textContent = audit.cryptographicSignature;
      }
    } catch {
      auditFingerprint.textContent = 'Local audit check error';
    }
  }

  // Initialize: Check backend status
  checkStatus();

  async function checkStatus() {
    try {
      const res = await fetch('/api/strip/status');
      if (res.ok) {
        const data = await res.json();
        if (data.mlInferenceOnline) {
          mlStatusDot.classList.remove('offline');
          mlStatusLabel.textContent = 'ML: SmolLM2 Active';
        } else {
          mlStatusDot.classList.add('offline');
          mlStatusLabel.textContent = 'Summarizer: Smart Extractive';
        }
      }
    } catch {
      mlStatusDot.classList.add('offline');
      mlStatusLabel.textContent = 'Offline Engine';
    }
  }

  // Tab switching
  tabUploadBtn.addEventListener('click', () => switchTab('upload'));
  tabPasteBtn.addEventListener('click', () => switchTab('paste'));

  function switchTab(tab) {
    if (tab === 'upload') {
      tabUploadBtn.classList.add('active');
      tabUploadBtn.setAttribute('aria-selected', 'true');
      tabPasteBtn.classList.remove('active');
      tabPasteBtn.setAttribute('aria-selected', 'false');
      panelUpload.classList.add('active');
      panelPaste.classList.remove('active');
    } else {
      tabPasteBtn.classList.add('active');
      tabPasteBtn.setAttribute('aria-selected', 'true');
      tabUploadBtn.classList.remove('active');
      tabUploadBtn.setAttribute('aria-selected', 'false');
      panelPaste.classList.add('active');
      panelUpload.classList.remove('active');
      pasteContent.focus();
    }
  }

  // Paste live char count
  pasteContent.addEventListener('input', () => {
    const len = pasteContent.value.length;
    charCount.textContent = `${len.toLocaleString()} characters`;
  });

  clearPasteBtn.addEventListener('click', () => {
    pasteContent.value = '';
    charCount.textContent = '0 characters';
    pasteContent.focus();
  });

  // Drag and Drop Handling
  ['dragenter', 'dragover'].forEach(eventName => {
    dropZone.addEventListener(eventName, (e) => {
      e.preventDefault();
      e.stopPropagation();
      dropZone.classList.add('dragover');
    }, false);
  });

  ['dragleave', 'drop'].forEach(eventName => {
    dropZone.addEventListener(eventName, (e) => {
      e.preventDefault();
      e.stopPropagation();
      dropZone.classList.remove('dragover');
    }, false);
  });

  dropZone.addEventListener('drop', (e) => {
    const dt = e.dataTransfer;
    const files = dt.files;
    if (files.length > 0) {
      handleFileUpload(files[0]);
    }
  });

  fileInput.addEventListener('change', (e) => {
    if (e.target.files && e.target.files.length > 0) {
      handleFileUpload(e.target.files[0]);
    }
  });

  // Drop Zone Keyboard & Click Accessibility
  dropZone.addEventListener('keydown', (e) => {
    if (e.key === 'Enter' || e.key === ' ') {
      e.preventDefault();
      fileInput.click();
    }
  });

  dropZone.addEventListener('click', (e) => {
    if (e.target !== fileInput) {
      fileInput.click();
    }
  });

  const nonEmailExtensions = [
    '.mp4', '.mov', '.avi', '.mkv', '.webm', '.wmv', '.m4v',
    '.mp3', '.wav', '.aac', '.flac', '.ogg',
    '.png', '.jpg', '.jpeg', '.gif', '.webp', '.bmp', '.tiff', '.svg',
    '.pdf', '.docx', '.xlsx', '.pptx', '.zip', '.tar', '.gz', '.7z', '.rar',
    '.exe', '.dmg', '.pkg', '.app', '.iso'
  ];

  function getFileExtension(name) {
    const idx = name.lastIndexOf('.');
    return idx !== -1 ? name.substring(idx).toLowerCase() : '';
  }

  closeErrorBtn.addEventListener('click', hideError);

  function showError(title, msg) {
    errorTitle.textContent = title || 'Invalid File Format';
    errorMessage.textContent = msg || 'An unexpected error occurred.';
    errorBanner.style.display = 'flex';
    announceToScreenReader(`Alert: ${errorTitle.textContent}. ${errorMessage.textContent}`);
    errorBanner.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
  }

  function hideError() {
    errorBanner.style.display = 'none';
  }

  // File Upload Ingestion
  async function handleFileUpload(file) {
    hideError();
    const ext = getFileExtension(file.name);
    if (nonEmailExtensions.includes(ext)) {
      showError(
        'Unsupported File Format',
        `The file "${file.name}" is not an email message (.eml). mailStripper is designed to ingest email messages and strip their attachments. If this is an attachment you wanted to extract, please upload the email (.eml) that contains it!`
      );
      fileInput.value = '';
      return;
    }

    showLoading(`Stripping "${file.name}"...`);

    const formData = new FormData();
    formData.append('file', file);

    try {
      const response = await fetch('/api/strip/file', {
        method: 'POST',
        body: formData
      });

      if (!response.ok) {
        const err = await response.json().catch(() => ({ error: 'Failed to parse email file.' }));
        showError('Invalid Email File', err.error || 'Failed to strip email.');
        showIngestion();
        return;
      }

      const data = await response.json();
      renderStrippedEmail(data);
    } catch (err) {
      console.error(err);
      showError('Network Error', 'Network or server error occurred while processing the email.');
      showIngestion();
    }
  }

  // Paste Text Ingestion
  submitPasteBtn.addEventListener('click', async () => {
    hideError();
    const text = pasteContent.value.trim();
    if (!text) {
      showError('Empty Content', 'Please paste some email text or headers first.');
      pasteContent.focus();
      return;
    }

    showLoading('Parsing raw email text...');

    try {
      const response = await fetch('/api/strip/text', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ content: text })
      });

      if (!response.ok) {
        const err = await response.json().catch(() => ({ error: 'Failed to parse pasted text.' }));
        showError('Invalid Email Content', err.error || 'Failed to strip email.');
        showIngestion();
        return;
      }

      const data = await response.json();
      renderStrippedEmail(data);
    } catch (err) {
      console.error(err);
      showError('Network Error', 'Network error while processing text.');
      showIngestion();
    }
  });

  // Load Sample Email
  loadSampleBtn.addEventListener('click', async () => {
    showLoading('Loading demo enterprise email...');
    try {
      const response = await fetch('/api/strip/sample');
      if (!response.ok) {
        throw new Error('Failed to load sample');
      }
      const data = await response.json();
      renderStrippedEmail(data);
    } catch (err) {
      console.error(err);
      alert('Could not load sample email.');
      showIngestion();
    }
  });

  // Render Stripped Email Results
  function renderStrippedEmail(data) {
    currentStrippedData = data;

    // Badges & Headers
    resSubjectPill.textContent = data.subject || '(No Subject)';
    resEnginePill.textContent = data.summaryEngine || 'Smart Extractive';

    // Hero One-Sentence Title
    resOneSentenceTitle.textContent = data.oneSentenceTitle || 'No summary title available.';

    // Short Summary & Bullets
    resBulletList.innerHTML = '';
    if (data.keyBulletPoints && data.keyBulletPoints.length > 0) {
      data.keyBulletPoints.forEach(point => {
        const li = document.createElement('li');
        li.textContent = point;
        resBulletList.appendChild(li);
      });
      resShortSummaryText.textContent = '';
      resShortSummaryText.style.display = 'none';
    } else {
      resShortSummaryText.textContent = data.shortSummary || 'No additional summary details.';
      resShortSummaryText.style.display = 'block';
    }

    // Metadata
    resFrom.textContent = data.from || 'Unknown';
    resFrom.title = data.from;
    resTo.textContent = data.to && data.to.length > 0 ? data.to.join(', ') : 'None specified';
    resTo.title = resTo.textContent;
    resDate.textContent = data.formattedDate || 'Not specified';
    resRawSubject.textContent = data.rawSubject || data.subject;
    resRawSubject.title = resRawSubject.textContent;
    resWordCount.textContent = `${data.wordCount} words`;

    // Attachments
    renderAttachments(data);

    // Body Inspector
    resBodyText.textContent = data.bodyText || '(No plain text body content found)';
    if (data.hasHtml && data.bodyHtml) {
      resBodyHtml.innerHTML = sanitizeHtmlPreview(data.bodyHtml);
      tabBodyHtml.style.display = 'inline-block';
    } else {
      resBodyHtml.innerHTML = '<p style="color:#64748b;">No HTML version present in this message.</p>';
      tabBodyHtml.style.display = 'none';
    }
    switchInspectorTab('text');

    showResults();
  }

  function renderAttachments(data) {
    attachmentsGrid.innerHTML = '';

    if (!data.attachments || data.attachments.length === 0) {
      resAttachmentStats.textContent = '0 attachments detected';
      downloadAllZipBtn.style.display = 'none';
      const emptyMsg = document.createElement('div');
      emptyMsg.className = 'empty-att-msg';
      emptyMsg.textContent = 'No attached files were found in this message.';
      attachmentsGrid.appendChild(emptyMsg);
      return;
    }

    resAttachmentStats.textContent = `${data.attachmentCount} file${data.attachmentCount === 1 ? '' : 's'} extracted • ${data.totalAttachmentSizeFormatted} total`;
    downloadAllZipBtn.style.display = 'inline-flex';
    downloadAllZipBtn.href = data.downloadAllZipUrl;

    data.attachments.forEach(att => {
      const card = document.createElement('div');
      card.className = 'att-card';
      card.tabIndex = 0;
      card.setAttribute('role', 'region');
      card.setAttribute('aria-label', `Attachment: ${escapeHtml(att.fileName)}, ${escapeHtml(att.formattedSize)}`);

      const catClass = `cat-${att.category || 'generic'}`;
      const extLabel = (att.fileExtension || 'bin').toUpperCase();

      card.innerHTML = `
        <div class="att-card-top">
          <div class="att-ext-badge ${catClass}">
            ${escapeHtml(extLabel)}
          </div>
          <div class="att-info">
            <h4 class="att-filename" title="${escapeHtml(att.fileName)}">${escapeHtml(att.fileName)}</h4>
            <span class="att-meta">${escapeHtml(att.formattedSize)} • ${escapeHtml(att.contentType)}</span>
          </div>
        </div>
        <div class="att-actions">
          <a class="att-btn-dl" href="${att.downloadUrl}" download="${escapeHtml(att.fileName)}" aria-label="Download ${escapeHtml(att.fileName)}">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"/><polyline points="7 10 12 15 17 10"/><line x1="12" y1="15" x2="12" y2="3"/></svg>
            Download
          </a>
          ${att.isPreviewable ? `
          <button class="att-btn-prev" data-index="${att.index}" title="Preview ${escapeHtml(att.fileName)}" aria-label="Preview ${escapeHtml(att.fileName)}">
            <svg width="15" height="15" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2"><path d="M2 12s3-7 10-7 10 7 10 7-3 7-10 7-10-7-10-7Z"/><circle cx="12" cy="12" r="3"/></svg>
          </button>` : ''}
        </div>
      `;

      if (att.isPreviewable) {
        const prevBtn = card.querySelector('.att-btn-prev');
        prevBtn.addEventListener('click', (e) => {
          e.stopPropagation();
          openPreviewModal(att, prevBtn);
        });
      }

      // Keyboard activation on card itself
      card.addEventListener('keydown', (e) => {
        if (e.target === card && (e.key === 'Enter' || e.key === ' ')) {
          e.preventDefault();
          if (att.isPreviewable) {
            openPreviewModal(att, card);
          } else {
            const dlLink = card.querySelector('.att-btn-dl');
            if (dlLink) dlLink.click();
          }
        }
      });

      attachmentsGrid.appendChild(card);
    });
  }

  // Preview Modal
  function openPreviewModal(att, triggerEl) {
    lastFocusedElement = triggerEl || document.activeElement;
    modalFileName.textContent = att.fileName;
    modalDownloadBtn.href = att.downloadUrl;
    modalDownloadBtn.download = att.fileName;
    modalDownloadBtn.setAttribute('aria-label', `Download ${att.fileName}`);
    modalBody.innerHTML = '<div class="spinner-ring" style="width:30px;height:30px;"></div>';
    previewModal.showModal();

    if (att.category === 'image') {
      const img = new Image();
      img.src = att.previewUrl;
      img.alt = `Preview of ${att.fileName}`;
      img.onload = () => {
        modalBody.innerHTML = '';
        modalBody.appendChild(img);
      };
      img.onerror = () => {
        modalBody.innerHTML = '<p style="color:#f43f5e;">Unable to display image preview.</p>';
      };
    } else {
      // Text or CSV
      fetch(att.previewUrl)
        .then(res => res.text())
        .then(txt => {
          modalBody.innerHTML = '';
          const pre = document.createElement('pre');
          pre.textContent = txt.length > 50000 ? txt.slice(0, 50000) + '\n...[truncated]' : txt;
          modalBody.appendChild(pre);
        })
        .catch(() => {
          modalBody.innerHTML = '<p style="color:#f43f5e;">Unable to load text preview.</p>';
        });
    }
  }

  closeModalBtn.addEventListener('click', () => {
    previewModal.close();
    if (lastFocusedElement) lastFocusedElement.focus();
  });

  previewModal.addEventListener('click', (e) => {
    if (e.target === previewModal) {
      previewModal.close();
      if (lastFocusedElement) lastFocusedElement.focus();
    }
  });

  previewModal.addEventListener('close', () => {
    if (lastFocusedElement) lastFocusedElement.focus();
  });

  // Body Inspector Tabs
  tabBodyText.addEventListener('click', () => switchInspectorTab('text'));
  tabBodyHtml.addEventListener('click', () => switchInspectorTab('html'));

  function switchInspectorTab(tab) {
    if (tab === 'text') {
      tabBodyText.classList.add('active');
      tabBodyHtml.classList.remove('active');
      panelBodyText.style.display = 'block';
      panelBodyHtml.style.display = 'none';
    } else {
      tabBodyHtml.classList.add('active');
      tabBodyText.classList.remove('active');
      panelBodyHtml.style.display = 'block';
      panelBodyText.style.display = 'none';
    }
  }

  // Copy to Clipboard Buttons
  copyTitleBtn.addEventListener('click', () => {
    if (currentStrippedData && currentStrippedData.oneSentenceTitle) {
      navigator.clipboard.writeText(currentStrippedData.oneSentenceTitle).then(() => {
        const originalText = copyTitleBtn.innerHTML;
        copyTitleBtn.innerHTML = '<span>✓ Copied!</span>';
        copyTitleBtn.setAttribute('aria-label', 'One-sentence title copied to clipboard');
        announceToScreenReader('One-sentence title copied to clipboard');
        setTimeout(() => {
          copyTitleBtn.innerHTML = originalText;
          copyTitleBtn.setAttribute('aria-label', 'Copy one-sentence title to clipboard');
        }, 1800);
      });
    }
  });

  copySummaryBtn.addEventListener('click', () => {
    if (currentStrippedData) {
      let text = currentStrippedData.oneSentenceTitle + '\n\n';
      if (currentStrippedData.keyBulletPoints && currentStrippedData.keyBulletPoints.length > 0) {
        text += currentStrippedData.keyBulletPoints.map(b => '• ' + b).join('\n');
      } else {
        text += currentStrippedData.shortSummary;
      }
      navigator.clipboard.writeText(text).then(() => {
        const orig = copySummaryBtn.innerHTML;
        copySummaryBtn.innerHTML = '✓';
        copySummaryBtn.setAttribute('aria-label', 'Summary copied to clipboard');
        announceToScreenReader('Summary copied to clipboard');
        setTimeout(() => {
          copySummaryBtn.innerHTML = orig;
          copySummaryBtn.setAttribute('aria-label', 'Copy short summary to clipboard');
        }, 1800);
      });
    }
  });

  // Strip Another Action
  stripAnotherBtn.addEventListener('click', () => {
    showIngestion();
    fileInput.value = '';
    pasteContent.value = '';
    charCount.textContent = '0 characters';
    currentStrippedData = null;
    announceToScreenReader('Reset to email ingestion screen.');
    if (tabUploadBtn.classList.contains('active')) {
      dropZone.focus();
    } else {
      pasteContent.focus();
    }
  });

  // View State Helpers
  function showLoading(status) {
    const text = status || 'Processing email...';
    loadingStatusText.textContent = text;
    announceToScreenReader(text);
    ingestionSection.style.display = 'none';
    resultsSection.style.display = 'none';
    loadingSection.style.display = 'flex';
  }

  function showResults() {
    loadingSection.style.display = 'none';
    ingestionSection.style.display = 'none';
    resultsSection.style.display = 'flex';
    window.scrollTo({ top: 0, behavior: 'smooth' });

    const titleText = resOneSentenceTitle.textContent.trim();
    announceToScreenReader(`Email stripped successfully. Results loaded: ${titleText}`);

    // Manage focus: move focus to the one-sentence title landmark
    setTimeout(() => {
      resOneSentenceTitle.focus();
    }, 100);
  }

  function showIngestion() {
    loadingSection.style.display = 'none';
    resultsSection.style.display = 'none';
    ingestionSection.style.display = 'block';
  }

  function escapeHtml(str) {
    if (!str) return '';
    return str.replace(/[&<>"']/g, m => ({
      '&': '&amp;',
      '<': '&lt;',
      '>': '&gt;',
      '"': '&quot;',
      "'": '&#39;'
    })[m]);
  }

  function sanitizeHtmlPreview(html) {
    const doc = new DOMParser().parseFromString(html, 'text/html');
    // Remove dangerous scripts and iframes
    doc.querySelectorAll('script, iframe, object, embed').forEach(el => el.remove());
    // Ensure all links open safely in new tab
    doc.querySelectorAll('a').forEach(a => {
      a.setAttribute('target', '_blank');
      a.setAttribute('rel', 'noopener noreferrer');
    });
    return doc.body.innerHTML;
  }
});
