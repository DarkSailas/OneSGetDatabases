
class MultiSelectDropdown {
  constructor(containerId, options = {}) {
    this.container = document.getElementById(containerId);
    if (!this.container) return;
    this.placeholder = this.container.dataset.placeholder || 'Все';
    this.trigger = this.container.querySelector('.multiselect-trigger');
    this.label = this.container.querySelector('.multiselect-label');
    this.badge = this.container.querySelector('.multiselect-badge');
    this.menu = this.container.querySelector('.multiselect-menu');
    this.searchInput = this.container.querySelector('.multiselect-search-input');
    this.optionsContainer = this.container.querySelector('.multiselect-options');
    this.selectAllBtn = this.container.querySelector('.select-all');
    this.clearAllBtn = this.container.querySelector('.clear-all');

    this.items = [];
    this.selected = new Set();
    this.onChange = options.onChange || (() => {});

    this.init();
  }

  init() {
    if (!this.trigger) return;
    this.trigger.addEventListener('click', (e) => {
      e.stopPropagation();
      const isOpen = this.container.classList.contains('open');
      document.querySelectorAll('.multiselect-dropdown.open').forEach(d => {
        if (d !== this.container) d.classList.remove('open');
      });
      this.container.classList.toggle('open', !isOpen);
      if (!isOpen && this.searchInput) {
        this.searchInput.value = '';
        this.filterOptions('');
        setTimeout(() => this.searchInput.focus(), 50);
      }
    });

    if (this.menu) {
      this.menu.addEventListener('click', (e) => e.stopPropagation());
    }

    if (this.searchInput) {
      this.searchInput.addEventListener('input', (e) => {
        this.filterOptions(e.target.value.trim().toLowerCase());
      });
    }

    if (this.selectAllBtn) {
      this.selectAllBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        this.selected = new Set(this.items.map(i => i.value));
        this.updateCheckboxes();
        this.updateUI();
        this.onChange(this.getSelected());
      });
    }

    if (this.clearAllBtn) {
      this.clearAllBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        this.selected.clear();
        this.updateCheckboxes();
        this.updateUI();
        this.onChange(this.getSelected());
      });
    }
  }

  setItems(items) {
    this.items = (items || []).map(i => typeof i === 'string' ? { value: i, label: i, count: null } : i);
    const validValues = new Set(this.items.map(i => i.value));
    this.selected = new Set([...this.selected].filter(v => validValues.has(v)));
    this.renderOptions();
    this.updateUI();
  }

  renderOptions() {
    if (!this.optionsContainer) return;
    if (this.items.length === 0) {
      this.optionsContainer.innerHTML = `<div style="padding: 10px; text-align: center; color: var(--color-warm-granite); font-size: 10.5px;">Нет элементов</div>`;
      return;
    }

    let html = '';
    this.items.forEach(item => {
      const isChecked = this.selected.has(item.value);
      const countHtml = item.count != null ? `<span class="multiselect-count">${item.count}</span>` : '';
      html += `
        <label class="multiselect-option ${isChecked ? 'checked' : ''}" data-val="${escapeHtml(item.value)}">
          <input type="checkbox" class="multiselect-checkbox" value="${escapeHtml(item.value)}" ${isChecked ? 'checked' : ''}>
          <span class="multiselect-option-text" title="${escapeHtml(item.label || item.value)}">${escapeHtml(item.label || item.value)}</span>
          ${countHtml}
        </label>
      `;
    });

    this.optionsContainer.innerHTML = html;

    this.optionsContainer.querySelectorAll('.multiselect-option').forEach(row => {
      row.addEventListener('click', (e) => {
        e.stopPropagation();
        const cb = row.querySelector('.multiselect-checkbox');
        if (e.target !== cb) {
          cb.checked = !cb.checked;
        }
        const val = cb.value;
        if (cb.checked) {
          this.selected.add(val);
          row.classList.add('checked');
        } else {
          this.selected.delete(val);
          row.classList.remove('checked');
        }
        this.updateUI();
        this.onChange(this.getSelected());
      });
    });
  }

  updateCheckboxes() {
    if (!this.optionsContainer) return;
    this.optionsContainer.querySelectorAll('.multiselect-option').forEach(row => {
      const cb = row.querySelector('.multiselect-checkbox');
      const isChecked = this.selected.has(cb.value);
      cb.checked = isChecked;
      row.classList.toggle('checked', isChecked);
    });
  }

  filterOptions(query) {
    if (!this.optionsContainer) return;
    this.optionsContainer.querySelectorAll('.multiselect-option').forEach(row => {
      const text = row.textContent.toLowerCase();
      row.style.display = !query || text.includes(query) ? 'flex' : 'none';
    });
  }

  updateUI() {
    const total = this.items.length;
    const count = this.selected.size;

    if (count === 0 || (total > 0 && count === total)) {
      this.label.textContent = this.placeholder;
      if (this.badge) this.badge.style.display = 'none';
    } else if (count === 1) {
      const val = [...this.selected][0];
      const item = this.items.find(i => i.value === val);
      this.label.textContent = item ? item.label : val;
      if (this.badge) this.badge.style.display = 'none';
    } else {
      const shortPrefix = this.placeholder.replace('Все ', '');
      this.label.textContent = `${shortPrefix}: ${count}`;
      if (this.badge) {
        this.badge.textContent = count;
        this.badge.style.display = 'inline-block';
      }
    }
  }

  getSelected() {
    if (this.selected.size === 0 || (this.items.length > 0 && this.selected.size === this.items.length)) {
      return [];
    }
    return [...this.selected];
  }

  setSelected(values) {
    this.selected = new Set(values || []);
    this.updateCheckboxes();
    this.updateUI();
  }
}

// Global click & Escape handlers for MultiSelect
window.addEventListener('click', (e) => {
  if (!e.target.closest('.multiselect-dropdown')) {
    document.querySelectorAll('.multiselect-dropdown.open').forEach(d => d.classList.remove('open'));
  }
});

window.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') {
    document.querySelectorAll('.multiselect-dropdown.open').forEach(d => d.classList.remove('open'));
  }
});

let state = {
  activeView: 'databases', // 'databases' or 'files'
  
  // Tab 1 (Databases) state
  page: 1,
  pageSize: 100000,
  totalPages: 1,
  total: 0,
  environment: 'ALL',
  search: '',
  cluster: '',
  sqlServer: '',
  platform: '',
  sortBy: 'cluster',
  sortDir: 'asc',
  items: [],
  selectedKeys: new Set(),
  
  // Tab 2 (Files) state
  filesPage: 1,
  filesPageSize: 100000,
  filesTotalPages: 1,
  filesTotal: 0,
  filesStatus: 'ALL',
  filesEnvironment: 'ALL',
  filesSearch: '',
  filesCluster: '',
  filesSqlServer: '',
  filesSortBy: 'size',
  filesSortDir: 'desc',
  fileItems: [],
  selectedFilesKeys: new Set(),

  // Tab 3 (Services) state
  servicesSortBy: 'displayName',
  servicesSortDir: 'asc',

  // Tab 4 (Audit) state
  auditSortBy: 'timestamp',
  auditSortDir: 'desc',

  selectedItem: null,
  currentDetails: null,
  lastScanTime: null,
  isScanning: false,
  showMetrics: true,
  currentAdGroupName: '',
  currentAdGroupDesc: '',
  currentAdGroupMembers: []
};

// DOM elements - Header & Global
const btnScan = document.getElementById('btnScan');
const liveStatusPulse = document.getElementById('liveStatusPulse');
const metricsStrip = document.getElementById('metricsStrip');
const btnToggleMetrics = document.getElementById('btnToggleMetrics');
const btnExportExcel = document.getElementById('btnExportExcel');
const btnExportJson = document.getElementById('btnExportJson');
const btnPrevPage = document.getElementById('btnPrevPage');
const btnNextPage = document.getElementById('btnNextPage');
const currentPageBadge = document.getElementById('currentPageBadge');
const paginationInfo = document.getElementById('paginationInfo');

// View Tab Elements
const tabBtnDatabases = document.getElementById('tabBtnDatabases');
const tabBtnFiles = document.getElementById('tabBtnFiles');
const tabBtnServices = document.getElementById('tabBtnServices');
const tabBtnAudit = document.getElementById('tabBtnAudit');
const toolbarDatabases = document.getElementById('toolbarDatabases');
const toolbarFiles = document.getElementById('toolbarFiles');
const toolbarServices = document.getElementById('toolbarServices');
const toolbarAudit = document.getElementById('toolbarAudit');
const databasesTableView = document.getElementById('databasesTableView');
const filesTableView = document.getElementById('filesTableView');
const servicesTableView = document.getElementById('servicesTableView');
const auditTableView = document.getElementById('auditTableView');
const databasesTableBody = document.getElementById('databasesTableBody');
const filesTableBody = document.getElementById('filesTableBody');
const servicesTableBody = document.getElementById('servicesTableBody');
const auditTableBody = document.getElementById('auditTableBody');
const selectAllCheckbox = document.getElementById('selectAllCheckbox');
const selectFilesAllCheckbox = document.getElementById('selectFilesAllCheckbox');

// Secret Services Filter Elements
const servicesSearchInput = document.getElementById('servicesSearchInput');
const servicesEnvSelect = document.getElementById('servicesEnvSelect');
const servicesStatusSelect = document.getElementById('servicesStatusSelect');
const btnRefreshServices = document.getElementById('btnRefreshServices');

// Secret Audit Filter Elements
const auditSearchInput = document.getElementById('auditSearchInput');
const btnRefreshAudit = document.getElementById('btnRefreshAudit');

// Service Confirm Modal Elements
const serviceConfirmModal = document.getElementById('serviceConfirmModal');
const confirmModalTitle = document.getElementById('confirmModalTitle');
const confirmModalClose = document.getElementById('confirmModalClose');
const confirmModalBodyText = document.getElementById('confirmModalBodyText');
const confirmModalWarning = document.getElementById('confirmModalWarning');
const confirmModalSpinner = document.getElementById('confirmModalSpinner');
const confirmModalSpinnerText = document.getElementById('confirmModalSpinnerText');
const confirmModalButtons = document.getElementById('confirmModalButtons');
const btnCancelServiceAction = document.getElementById('btnCancelServiceAction');
const btnExecuteServiceAction = document.getElementById('btnExecuteServiceAction');


// Tab 1 Filter Elements
const searchInput = document.getElementById('searchInput');
const envSelect = document.getElementById('envSelect');
const pageSizeSelect = document.getElementById('pageSizeSelect');

// MultiSelect Controllers (Tab 1)
const msCluster = new MultiSelectDropdown('clusterDropdown', {
  onChange: (vals) => {
    state.cluster = vals.join(',');
    state.page = 1;
    loadDatabases();
  }
});

const msSql = new MultiSelectDropdown('sqlDropdown', {
  onChange: (vals) => {
    state.sqlServer = vals.join(',');
    state.page = 1;
    loadDatabases();
  }
});

const msPlatform = new MultiSelectDropdown('platformDropdown', {
  onChange: (vals) => {
    state.platform = vals.join(',');
    state.page = 1;
    loadDatabases();
  }
});

// Tab 2 Filter Elements
const filesSearchInput = document.getElementById('filesSearchInput');
const filesEnvSelect = document.getElementById('filesEnvSelect');
const filesPageSizeSelect = document.getElementById('filesPageSizeSelect');

// MultiSelect Controllers (Tab 2)
const msFilesCluster = new MultiSelectDropdown('filesClusterDropdown', {
  onChange: (vals) => {
    state.filesCluster = vals.join(',');
    state.filesPage = 1;
    loadFiles();
  }
});

const msFilesSql = new MultiSelectDropdown('filesSqlDropdown', {
  onChange: (vals) => {
    state.filesSqlServer = vals.join(',');
    state.filesPage = 1;
    loadFiles();
  }
});

// Selection Bar Elements
const selectionActionBar = document.getElementById('selectionActionBar');
const selectedCountBadge = document.getElementById('selectedCountBadge');
const btnCopySelected = document.getElementById('btnCopySelected');
const btnExportSelectedExcel = document.getElementById('btnExportSelectedExcel');
const btnExportSelectedJson = document.getElementById('btnExportSelectedJson');
const btnClearSelection = document.getElementById('btnClearSelection');

// Details Modal Elements
const detailsModal = document.getElementById('detailsModal');
const modalClose = document.getElementById('modalClose');
const modalTitle = document.getElementById('modalTitle');
const modalSubtitle = document.getElementById('modalSubtitle');
const clusterInfoGrid = document.getElementById('clusterInfoGrid');
const dbmsSummaryGrid = document.getElementById('dbmsSummaryGrid');
const dbmsFilesTableBody = document.getElementById('dbmsFilesTableBody');
const dbmsUsersTableBody = document.getElementById('dbmsUsersTableBody');
const infraInfoGrid = document.getElementById('infraInfoGrid');
const dbmsLoadingState = document.getElementById('dbmsLoadingState');
const dbmsContentState = document.getElementById('dbmsContentState');

// AD Group Modal Elements
const adGroupModal = document.getElementById('adGroupModal');
const adModalClose = document.getElementById('adModalClose');
const adModalGroupName = document.getElementById('adModalGroupName');
const adModalMemberCount = document.getElementById('adModalMemberCount');
const adModalGroupDesc = document.getElementById('adModalGroupDesc');
const adModalLoading = document.getElementById('adModalLoading');
const adModalContent = document.getElementById('adModalContent');
const adMembersTableBody = document.getElementById('adMembersTableBody');
const adMemberSearchInput = document.getElementById('adMemberSearchInput');

function showToast(message, type = 'info') {
  const container = document.getElementById('toastContainer');
  if (!container) return;
  const toast = document.createElement('div');
  toast.className = 'toast';
  if (type === 'error') {
    toast.style.borderColor = 'rgba(238, 96, 24, 0.6)';
    toast.style.color = 'var(--color-signal-orange)';
  } else {
    toast.style.borderColor = 'rgba(160, 202, 146, 0.4)';
    toast.style.color = 'var(--color-metric-green)';
  }
  
  toast.textContent = message;
  container.appendChild(toast);
  setTimeout(() => {
    toast.remove();
  }, 3500);
}

function getKey(item) {
  return `${item.environment}:${item.cluster}:${item.name}`;
}

function getFileKey(item) {
  return `${item.environment}:${item.sqlServer}:${item.sqlDbName}:${item.name}`;
}

// View Tabs Switching
function switchView(viewName) {
  state.activeView = viewName;
  [tabBtnDatabases, tabBtnFiles, tabBtnServices, tabBtnAudit].forEach(b => {
    if (b) b.classList.toggle('active', b.dataset.view === viewName);
  });
  if (toolbarDatabases) toolbarDatabases.style.display = viewName === 'databases' ? 'flex' : 'none';
  if (toolbarFiles) toolbarFiles.style.display = viewName === 'files' ? 'flex' : 'none';
  if (toolbarServices) toolbarServices.style.display = viewName === 'services' ? 'flex' : 'none';
  if (toolbarAudit) toolbarAudit.style.display = viewName === 'audit' ? 'flex' : 'none';

  if (databasesTableView) databasesTableView.style.display = viewName === 'databases' ? 'block' : 'none';
  if (filesTableView) filesTableView.style.display = viewName === 'files' ? 'block' : 'none';
  if (servicesTableView) servicesTableView.style.display = viewName === 'services' ? 'block' : 'none';
  if (auditTableView) auditTableView.style.display = viewName === 'audit' ? 'block' : 'none';

  updateSelectionBar();
  renderPagination();

  if (viewName === 'databases') {
    makeTableResizable(document.getElementById('mainDatabasesTable'));
  } else if (viewName === 'files') {
    makeTableResizable(document.getElementById('filesDatabasesTable'));
  } else if (viewName === 'services') {
    makeTableResizable(document.getElementById('servicesTable'));
  } else if (viewName === 'audit') {
    makeTableResizable(document.getElementById('auditTable'));
  }

  if (viewName === 'databases' && (!state.items || state.items.length === 0)) {
    loadDatabases();
  } else if (viewName === 'files' && (!state.fileItems || state.fileItems.length === 0)) {
    loadFiles();
  } else if (viewName === 'services' && (!state.servicesList || state.servicesList.length === 0)) {
    loadServices();
  } else if (viewName === 'audit' && (!state.auditList || state.auditList.length === 0)) {
    loadAuditLogs();
  }
}

tabBtnDatabases.addEventListener('click', () => switchView('databases'));
tabBtnFiles.addEventListener('click', () => switchView('files'));
if (tabBtnServices) tabBtnServices.addEventListener('click', () => switchView('services'));
if (tabBtnAudit) tabBtnAudit.addEventListener('click', () => switchView('audit'));

function loadCurrentView() {
  if (state.activeView === 'databases') {
    loadDatabases();
  } else if (state.activeView === 'files') {
    loadFiles();
  } else if (state.activeView === 'services') {
    loadServices();
  } else if (state.activeView === 'audit') {
    loadAuditLogs();
  }
}

// Load UI Config
async function loadConfig() {
  try {
    const res = await fetch('/api/databases/config');
    if (res.ok) {
      const cfg = await res.json();
      const stored = localStorage.getItem('showMetrics');
      state.showMetrics = stored !== null ? (stored === 'true') : (cfg.showMetrics ?? true);
      applyMetricsVisibility();

      if (cfg.buildDate) {
        const buildLabel = document.getElementById('buildDateLabel');
        if (buildLabel) buildLabel.textContent = cfg.buildDate;
      }
    }
  } catch {
    const stored = localStorage.getItem('showMetrics');
    if (stored !== null) {
      state.showMetrics = (stored === 'true');
      applyMetricsVisibility();
    }
  }
}

function applyMetricsVisibility() {
  if (state.showMetrics) {
    metricsStrip.classList.remove('collapsed');
    btnToggleMetrics.classList.add('btn-active');
    btnToggleMetrics.classList.remove('btn-ghost');
  } else {
    metricsStrip.classList.add('collapsed');
    btnToggleMetrics.classList.remove('btn-active');
    btnToggleMetrics.classList.add('btn-ghost');
  }
}

btnToggleMetrics.addEventListener('click', () => {
  state.showMetrics = !state.showMetrics;
  localStorage.setItem('showMetrics', state.showMetrics.toString());
  applyMetricsVisibility();
});

// Load Stats and Check for Background Updates
async function loadStats(silent = false) {
  try {
    const res = await fetch('/api/databases/stats');
    if (!res.ok) return;
    const stats = await res.json();

    document.getElementById('metricTotal').textContent = stats.totalDatabases;
    document.getElementById('metricDevProd').textContent = `PROD: ${stats.prodDatabases} | DEV: ${stats.devDatabases}`;
    const totalClusters = stats.totalClusters || stats.uniqueClusters;
    const btnCount = document.getElementById('btnClusterHealthCount');
    if (btnCount) {
      btnCount.textContent = totalClusters ? `(${totalClusters})` : '';
    }
    document.getElementById('metricClusters').textContent = totalClusters;
    const clustersSub = document.getElementById('metricClustersSub');
    if (clustersSub) {
      if (stats.totalClusters && stats.uniqueClusters && stats.totalClusters !== stats.uniqueClusters) {
        clustersSub.textContent = `${stats.uniqueClusters} онлайн | ${stats.totalClusters - stats.uniqueClusters} пустых`;
      } else {
        clustersSub.textContent = 'Диагностика ➔';
      }
    }
    document.getElementById('metricSqlServers').textContent = stats.uniqueSqlServers;
    const sqlSub = document.getElementById('metricSqlSub');
    if (sqlSub && stats.sqlServersSubtitle) {
      sqlSub.textContent = stats.sqlServersSubtitle;
    }
    document.getElementById('metricAdCoverage').textContent = `${stats.accessGroupCoveragePercent}%`;
    document.getElementById('metricAdCount').textContent = `${stats.withAccessGroupCount} с группами AD`;

    const rawScanTime = stats.lastScanTime;
    const isNewScan = state.lastScanTime && rawScanTime && rawScanTime !== state.lastScanTime && rawScanTime !== '0001-01-01T00:00:00';

    if (rawScanTime && rawScanTime !== '0001-01-01T00:00:00') {
      const dt = new Date(rawScanTime);
      document.getElementById('metricLastScan').textContent = `Опрос: ${dt.toLocaleTimeString('ru-RU')}`;
      liveStatusPulse.classList.remove('pulse-orange');
    } else {
      document.getElementById('metricLastScan').textContent = `Опрос: первичный сбор...`;
      liveStatusPulse.classList.add('pulse-orange');
    }

    state.lastScanTime = rawScanTime;

    if (isNewScan) {
      loadCurrentView();
      loadFilters();
      if (!silent) {
        showToast('Данные баз 1С автоматически обновлены.', 'success');
      }
    }
  } catch (err) {
    liveStatusPulse.classList.add('pulse-orange');
  }
}

// Load Filters
async function loadFilters() {
  try {
    const res = await fetch('/api/databases/filters');
    if (!res.ok) return;
    const data = await res.json();

    // Populate Tab 1 MultiSelects
    if (typeof msCluster !== 'undefined' && msCluster) msCluster.setItems(data.clusters || []);
    if (typeof msSql !== 'undefined' && msSql) msSql.setItems(data.sqlServers || []);
    if (typeof msPlatform !== 'undefined' && msPlatform) msPlatform.setItems(data.platforms || []);

    // Populate Tab 2 MultiSelects
    if (typeof msFilesCluster !== 'undefined' && msFilesCluster) msFilesCluster.setItems(data.clusters || []);
    if (typeof msFilesSql !== 'undefined' && msFilesSql) msFilesSql.setItems(data.sqlServers || []);
  } catch (err) {
    console.error('Failed to load filters:', err);
  }
}

// Load Databases (Tab 1)
async function loadDatabases() {
  try {
    const params = new URLSearchParams({
      page: state.page,
      pageSize: state.pageSize,
      environment: state.environment,
      search: state.search,
      cluster: state.cluster,
      sqlServer: state.sqlServer,
      platform: state.platform,
      sortBy: state.sortBy,
      sortDir: state.sortDir
    });

    if (!state.items || state.items.length === 0) {
      databasesTableBody.innerHTML = `<tr class="no-hover"><td colspan="11" style="text-align: center; padding: 0;"><div class="loading-container"><span class="spinner spinner-lg"></span><span>Загрузка информационных баз 1С...</span></div></td></tr>`;
    } else {
      databasesTableBody.style.opacity = '0.5';
    }

    const res = await fetch(`/api/databases?${params.toString()}`);
    databasesTableBody.style.opacity = '1';

    if (!res.ok) {
      databasesTableBody.innerHTML = `<tr class="no-hover"><td colspan="11" style="text-align: center; color: var(--color-signal-orange); padding: 25px;">Ошибка загрузки данных (${res.status})</td></tr>`;
      return;
    }

    const data = await res.json();
    state.items = data.items;
    state.total = data.total;
    state.totalPages = data.totalPages;

    renderTable();
    renderPagination();
    updateSortHeaders();
    updateSelectionBar();
  } catch (err) {
    databasesTableBody.style.opacity = '1';
    console.error('Failed to load databases:', err);
    databasesTableBody.innerHTML = `<tr class="no-hover"><td colspan="11" style="text-align: center; color: var(--color-signal-orange); padding: 25px;">Сетевая ошибка при обращении к веб-сервису</td></tr>`;
  }
}

// Load Files & Sizes (Tab 2)
async function loadFiles() {
  try {
    const params = new URLSearchParams({
      page: state.filesPage,
      pageSize: state.filesPageSize,
      status: state.filesStatus,
      environment: state.filesEnvironment,
      search: state.filesSearch,
      cluster: state.filesCluster,
      sqlServer: state.filesSqlServer,
      sortBy: state.filesSortBy,
      sortDir: state.filesSortDir
    });

    if (!state.fileItems || state.fileItems.length === 0) {
      filesTableBody.innerHTML = `<tr class="no-hover"><td colspan="10" style="text-align: center; padding: 0;"><div class="loading-container"><span class="spinner spinner-lg"></span><span>Сбор сведений о размерах и файлах баз данных СУБД...</span></div></td></tr>`;
    } else {
      filesTableBody.style.opacity = '0.5';
    }

    const res = await fetch(`/api/databases/files?${params.toString()}`);
    filesTableBody.style.opacity = '1';

    if (!res.ok) {
      filesTableBody.innerHTML = `<tr class="no-hover"><td colspan="10" style="text-align: center; color: var(--color-signal-orange); padding: 25px;">Ошибка загрузки файлов СУБД (${res.status})</td></tr>`;
      return;
    }

    const data = await res.json();
    state.fileItems = data.items;
    state.filesTotal = data.total;
    state.filesTotalPages = data.totalPages;

    renderFilesTable();
    renderPagination();
    updateFilesSortHeaders();
  } catch (err) {
    filesTableBody.style.opacity = '1';
    console.error('Failed to load files:', err);
    filesTableBody.innerHTML = `<tr class="no-hover"><td colspan="10" style="text-align: center; color: var(--color-signal-orange); padding: 25px;">Сетевая ошибка при запросе файлов СУБД</td></tr>`;
  }
}

function updateSortHeaders() {
  document.querySelectorAll('th.sortable').forEach(th => {
    const field = th.dataset.sort;
    const icon = th.querySelector('.sort-icon');
    if (field === state.sortBy) {
      th.classList.add('sorted');
      if (icon) icon.textContent = state.sortDir === 'asc' ? '▲' : '▼';
    } else {
      th.classList.remove('sorted');
      if (icon) icon.textContent = '⇅';
    }
  });
}

function updateFilesSortHeaders() {
  document.querySelectorAll('th.sortable-files').forEach(th => {
    const field = th.dataset.sort;
    const icon = th.querySelector('.sort-icon');
    if (field === state.filesSortBy) {
      th.classList.add('sorted');
      if (icon) icon.textContent = state.filesSortDir === 'asc' ? '▲' : '▼';
    } else {
      th.classList.remove('sorted');
      if (icon) icon.textContent = '⇅';
    }
  });
}

function renderTable() {
  if (!state.items || state.items.length === 0) {
    const isScanning = state.isScanning || !state.lastScanTime || state.lastScanTime === '0001-01-01T00:00:00';
    if (isScanning) {
      databasesTableBody.innerHTML = `
        <tr>
          <td colspan="11" style="text-align: center; padding: 40px 20px;">
            <div class="loading-container">
              <span class="spinner spinner-lg"></span>
              <div style="font-size: 13px; font-weight: 500; color: var(--color-bone); margin-top: 4px;">Выполняется опрос кластеров 1С...</div>
              <div style="font-size: 11px; color: var(--color-warm-granite);">Идет сбор информационных баз, групп доступа и СУБД. Данные появятся автоматически.</div>
            </div>
          </td>
        </tr>
      `;
    } else {
      databasesTableBody.innerHTML = `<tr class="no-hover"><td colspan="11" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">Базы 1С не найдены в кэше сервиса</td></tr>`;
    }
    selectAllCheckbox.checked = false;
    return;
  }

  let html = '';
  const startIdx = (state.page - 1) * state.pageSize;
  let allCurrentPageSelected = true;

  state.items.forEach((b, index) => {
    const key = getKey(b);
    const isSelected = state.selectedKeys.has(key);
    if (!isSelected) allCurrentPageSelected = false;

    const isProd = b.environment === 'PROD';
    const envBadge = `<span class="badge ${isProd ? 'badge-prod' : 'badge-dev'}">${b.environment}</span>`;
    
    const hasAd = b.accessGroup && b.accessGroup !== 'Отсутствует' && b.accessGroup !== '—';
    const adBadge = hasAd
      ? `<span class="badge badge-ok badge-clickable" title="Нажмите, чтобы просмотреть состав группы ${escapeHtml(b.accessGroup)}" onclick="event.stopPropagation(); openAdGroup('${escapeHtml(b.accessGroup)}')">${escapeHtml(b.accessGroup)}</span>`
      : `<span class="badge badge-missing">Отсутствует</span>`;

    html += `
      <tr class="${isSelected ? 'selected-row' : ''}" onclick="onRowClick(event, ${index})">
        <td style="text-align: center;" onclick="event.stopPropagation();">
          <input type="checkbox" class="row-checkbox" ${isSelected ? 'checked' : ''} onchange="toggleRowSelection(${index}, this.checked)">
        </td>
        <td class="mono" style="color: var(--color-warm-granite); text-align: center;">${startIdx + index + 1}</td>
        <td class="${state.sortBy === 'env' ? 'col-sorted' : ''}">${envBadge}</td>
        <td class="${state.sortBy === 'name' ? 'col-sorted' : ''}"><strong style="color: var(--color-bone); font-weight: 500;" title="${escapeHtml(b.name)}">${escapeHtml(b.name)}</strong></td>
        <td class="cell-truncate ${state.sortBy === 'description' ? 'col-sorted' : ''}" title="${escapeHtml(b.description || '')}">${escapeHtml(b.description || '—')}</td>
        <td class="${state.sortBy === 'cluster' ? 'col-sorted' : ''}"><code>${escapeHtml(b.cluster)}</code></td>
        <td class="${state.sortBy === 'platform' ? 'col-sorted' : ''}"><span class="badge badge-neutral">${escapeHtml(b.platform || '—')}</span></td>
        <td class="cell-truncate ${state.sortBy === 'sql' ? 'col-sorted' : ''}" title="${escapeHtml(b.sql)}"><code>${escapeHtml(b.sql)}</code></td>
        <td class="cell-truncate ${state.sortBy === 'sqldbname' ? 'col-sorted' : ''}" title="${escapeHtml(b.sqlDbName)}">${escapeHtml(b.sqlDbName)}</td>
        <td class="cell-truncate ${state.sortBy === 'accessgroup' ? 'col-sorted' : ''}" title="${escapeHtml(b.accessGroup)}">${adBadge}</td>
        <td style="text-align: center;" onclick="event.stopPropagation();">
          <button class="btn btn-dark btn-sm" style="padding: 2px 8px; font-size: 10.5px;" title="Свойства базы и инспекция СУБД" onclick="openDetails('${b.environment}', '${b.cluster}', '${escapeHtml(b.name)}')">
            Подробнее
          </button>
        </td>
      </tr>
    `;
  });

  databasesTableBody.innerHTML = html;
  selectAllCheckbox.checked = state.items.length > 0 && allCurrentPageSelected;
}

function renderFilePaths(pathString) {
  if (!pathString || pathString === '—') return '<span style="color: var(--color-warm-granite);">—</span>';
  const paths = pathString.split(';').map(p => p.trim()).filter(p => p.length > 0);
  if (paths.length === 0) return '<span style="color: var(--color-warm-granite);">—</span>';
  return paths.map(p => `<div style="white-space: nowrap; padding: 1px 0;"><code style="font-size: 10.5px;">${escapeHtml(p)}</code></div>`).join('');
}

function renderFilesTable() {
  if (!state.fileItems || state.fileItems.length === 0) {
    const isScanning = state.isScanning || !state.lastScanTime || state.lastScanTime === '0001-01-01T00:00:00';
    if (isScanning) {
      filesTableBody.innerHTML = `
        <tr>
          <td colspan="10" style="text-align: center; padding: 40px 20px;">
            <div class="loading-container">
              <span class="spinner spinner-lg"></span>
              <div style="font-size: 13px; font-weight: 500; color: var(--color-bone); margin-top: 4px;">Сбор сведений о размерах и файлах баз данных СУБД...</div>
              <div style="font-size: 11px; color: var(--color-warm-granite);">Опрашиваются серверы MS SQL и PostgreSQL. Данные обновятся автоматически.</div>
            </div>
          </td>
        </tr>
      `;
    } else {
      filesTableBody.innerHTML = `<tr class="no-hover"><td colspan="10" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">Файлы баз данных не найдены</td></tr>`;
    }
    if (selectFilesAllCheckbox) selectFilesAllCheckbox.checked = false;
    return;
  }

  let html = '';
  const startIdx = (state.filesPage - 1) * state.filesPageSize;
  let allCurrentPageSelected = true;

  state.fileItems.forEach((f, index) => {
    const key = getFileKey(f);
    const isSelected = state.selectedFilesKeys.has(key);
    if (!isSelected) allCurrentPageSelected = false;

    const isMissing = !f.totalSizeBytes || f.totalSizeBytes === 0 || f.totalSizeGb === 0;
    const isProd = f.environment === 'PROD';
    const envBadge = `<span class="badge ${isProd ? 'badge-prod' : 'badge-dev'}">${f.environment}</span>`;

    const rowClasses = [
      isSelected ? 'selected-row' : '',
      isMissing ? 'row-missing-dbms' : ''
    ].filter(Boolean).join(' ');

    const sizeCellHtml = isMissing
      ? `<span class="badge badge-purple" title="База зарегистрирована в кластере 1С, но физически отсутствует на сервере СУБД">Нет в СУБД</span>`
      : `<strong class="mono" style="color: var(--color-metric-green); font-size: 11.5px;">${f.totalSizeGb}</strong>`;

    const dataPathsHtml = isMissing
      ? `<span style="color: #c084fc; font-size: 10px; font-style: italic; opacity: 0.85;">База не найдена на сервере</span>`
      : renderFilePaths(f.dataFilesPath);

    const logPathsHtml = isMissing
      ? `<span style="color: #c084fc; font-size: 10px; font-style: italic; opacity: 0.85;">—</span>`
      : renderFilePaths(f.logFilesPath);

    html += `
      <tr class="${rowClasses}" onclick="onFilesRowClick(event, ${index})">
        <td style="text-align: center;" onclick="event.stopPropagation();">
          <input type="checkbox" class="row-checkbox" ${isSelected ? 'checked' : ''} onchange="toggleFilesRowSelection(${index}, this.checked)">
        </td>
        <td class="mono" style="color: var(--color-warm-granite); text-align: center;">${startIdx + index + 1}</td>
        <td class="${state.filesSortBy === 'env' ? 'col-sorted' : ''}">${envBadge}</td>
        <td class="${state.filesSortBy === 'name' ? 'col-sorted' : ''}"><strong style="color: ${isMissing ? '#e9d5ff' : 'var(--color-bone)'}; font-weight: 500;" title="${escapeHtml(f.name)}">${escapeHtml(f.name)}</strong></td>
        <td class="${state.filesSortBy === 'cluster' ? 'col-sorted' : ''}"><code>${escapeHtml(f.cluster || '—')}</code></td>
        <td class="${state.filesSortBy === 'sql' ? 'col-sorted' : ''}"><code>${escapeHtml(f.sqlServer)}</code></td>
        <td class="${state.filesSortBy === 'sqldbname' ? 'col-sorted' : ''}"><strong style="color: ${isMissing ? '#e9d5ff' : 'var(--color-bone)'}; font-weight: 500;">${escapeHtml(f.sqlDbName)}</strong></td>
        <td class="${state.filesSortBy === 'size' ? 'col-sorted' : ''}" style="text-align: right;">${sizeCellHtml}</td>
        <td>${dataPathsHtml}</td>
        <td>${logPathsHtml}</td>
      </tr>
    `;
  });

  filesTableBody.innerHTML = html;
  if (selectFilesAllCheckbox) {
    selectFilesAllCheckbox.checked = state.fileItems.length > 0 && allCurrentPageSelected;
  }
}

function renderPagination() {
  const isDb = state.activeView === 'databases';
  const page = isDb ? state.page : state.filesPage;
  const pageSize = isDb ? state.pageSize : state.filesPageSize;
  const total = isDb ? state.total : state.filesTotal;
  const totalPages = isDb ? state.totalPages : state.filesTotalPages;

  const start = total === 0 ? 0 : (page - 1) * pageSize + 1;
  const end = Math.min(page * pageSize, total);
  paginationInfo.textContent = `Показано ${start}-${end} из ${total} ${isDb ? 'баз' : 'записей'}`;

  currentPageBadge.textContent = `${page} / ${Math.max(1, totalPages)}`;
  btnPrevPage.disabled = page <= 1;
  btnNextPage.disabled = page >= totalPages;
}

// Row Selection Logic for Databases Table
window.toggleRowSelection = function(index, checked) {
  const item = state.items[index];
  if (!item) return;
  const key = getKey(item);

  if (checked) {
    state.selectedKeys.add(key);
  } else {
    state.selectedKeys.delete(key);
  }

  renderTable();
  updateSelectionBar();
};

window.onRowClick = function(event, index) {
  const selection = window.getSelection();
  if (selection && selection.toString().length > 0) return;

  const item = state.items[index];
  if (!item) return;
  const key = getKey(item);

  if (state.selectedKeys.has(key)) {
    state.selectedKeys.delete(key);
  } else {
    state.selectedKeys.add(key);
  }

  renderTable();
  updateSelectionBar();
};

// Row Selection Logic for Files Table
window.toggleFilesRowSelection = function(index, checked) {
  const item = state.fileItems[index];
  if (!item) return;
  const key = getFileKey(item);

  if (checked) {
    state.selectedFilesKeys.add(key);
  } else {
    state.selectedFilesKeys.delete(key);
  }

  renderFilesTable();
  updateSelectionBar();
};

window.onFilesRowClick = function(event, index) {
  const selection = window.getSelection();
  if (selection && selection.toString().length > 0) return;

  const item = state.fileItems[index];
  if (!item) return;
  const key = getFileKey(item);

  if (state.selectedFilesKeys.has(key)) {
    state.selectedFilesKeys.delete(key);
  } else {
    state.selectedFilesKeys.add(key);
  }

  renderFilesTable();
  updateSelectionBar();
};

// Select All Checkbox Handlers
if (selectAllCheckbox) {
  selectAllCheckbox.addEventListener('change', (e) => {
    const checked = e.target.checked;
    state.items.forEach(item => {
      const key = getKey(item);
      if (checked) {
        state.selectedKeys.add(key);
      } else {
        state.selectedKeys.delete(key);
      }
    });
    renderTable();
    updateSelectionBar();
  });
}

if (selectFilesAllCheckbox) {
  selectFilesAllCheckbox.addEventListener('change', (e) => {
    const checked = e.target.checked;
    state.fileItems.forEach(item => {
      const key = getFileKey(item);
      if (checked) {
        state.selectedFilesKeys.add(key);
      } else {
        state.selectedFilesKeys.delete(key);
      }
    });
    renderFilesTable();
    updateSelectionBar();
  });
}

btnClearSelection.addEventListener('click', () => {
  if (state.activeView === 'databases') {
    state.selectedKeys.clear();
    renderTable();
  } else {
    state.selectedFilesKeys.clear();
    renderFilesTable();
  }
  updateSelectionBar();
});

function updateSelectionBar() {
  const isDb = state.activeView === 'databases';
  const count = isDb ? state.selectedKeys.size : state.selectedFilesKeys.size;
  if (count > 0) {
    selectedCountBadge.textContent = `Выбрано: ${count}`;
    selectionActionBar.classList.add('visible');
  } else {
    selectionActionBar.classList.remove('visible');
  }
}

function getSelectedDatabaseObjects() {
  return state.items.filter(item => state.selectedKeys.has(getKey(item)));
}

function getSelectedFilesObjects() {
  return state.fileItems.filter(item => state.selectedFilesKeys.has(getFileKey(item)));
}

// Copy Selected rows to Clipboard (Supports both tabs)
btnCopySelected.addEventListener('click', async () => {
  if (state.activeView === 'databases') {
    const selected = getSelectedDatabaseObjects();
    if (selected.length === 0) return;

    const header = "Среда\tБаза 1С\tОписание\tКластер\tПлатформа\tСервер СУБД\tБаза в СУБД\tГруппа AD\tIP сервера";
    const rows = selected.map(b => 
      `${b.environment}\t${b.name}\t${b.description || ''}\t${b.cluster}\t${b.platform || ''}\t${b.sql}\t${b.sqlDbName}\t${b.accessGroup}\t${b.serverIP}`
    );

    const tsv = [header, ...rows].join('\n');
    try {
      await navigator.clipboard.writeText(tsv);
      showToast(`Скопировано ${selected.length} строк баз 1С в буфер обмена`, 'success');
    } catch {
      showToast('Не удалось скопировать в буфер обмена', 'error');
    }
  } else {
    const selected = getSelectedFilesObjects();
    if (selected.length === 0) return;

    const header = "Среда\tБаза 1С\tКластер 1С\tСервер СУБД\tБаза в СУБД\tОбщий размер (GB)\tФайлы данных (MDF / NDF)\tФайл журнала (LDF)";
    const rows = selected.map(f => 
      `${f.environment}\t${f.name}\t${f.cluster || ''}\t${f.sqlServer}\t${f.sqlDbName}\t${f.totalSizeGb}\t${f.dataFilesPath}\t${f.logFilesPath}`
    );

    const tsv = [header, ...rows].join('\n');
    try {
      await navigator.clipboard.writeText(tsv);
      showToast(`Скопировано ${selected.length} строк файлов СУБД в буфер обмена`, 'success');
    } catch {
      showToast('Не удалось скопировать в буфер обмена', 'error');
    }
  }
});

// Export Selected to Excel (Supports both tabs)
btnExportSelectedExcel.addEventListener('click', async () => {
  if (state.activeView === 'databases') {
    const selected = getSelectedDatabaseObjects();
    if (selected.length === 0) return;

    try {
      const res = await fetch('/api/export/excel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(selected)
      });

      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_Databases_Selected_${formatDate(new Date())}.xls`);
        showToast(`Выгружено ${selected.length} строк в Excel`, 'success');
      }
    } catch (err) {
      showToast(`Ошибка экспорта: ${err.message}`, 'error');
    }
  } else {
    const selected = getSelectedFilesObjects();
    if (selected.length === 0) return;

    try {
      const res = await fetch('/api/export/files/excel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(selected)
      });

      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_DBMS_Files_Selected_${formatDate(new Date())}.xls`);
        showToast(`Выгружено ${selected.length} строк файлов СУБД в Excel`, 'success');
      }
    } catch (err) {
      showToast(`Ошибка экспорта: ${err.message}`, 'error');
    }
  }
});

// Export Selected to JSON (Supports both tabs)
btnExportSelectedJson.addEventListener('click', async () => {
  if (state.activeView === 'databases') {
    const selected = getSelectedDatabaseObjects();
    if (selected.length === 0) return;

    try {
      const res = await fetch('/api/export/json', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(selected)
      });

      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_Databases_Selected_${formatDate(new Date())}.json`);
        showToast(`Выгружено ${selected.length} строк в JSON`, 'success');
      }
    } catch (err) {
      showToast(`Ошибка экспорта: ${err.message}`, 'error');
    }
  } else {
    const selected = getSelectedFilesObjects();
    if (selected.length === 0) return;

    try {
      const res = await fetch('/api/export/files/json', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(selected)
      });

      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_DBMS_Files_Selected_${formatDate(new Date())}.json`);
        showToast(`Выгружено ${selected.length} строк файлов СУБД в JSON`, 'success');
      }
    } catch (err) {
      showToast(`Ошибка экспорта: ${err.message}`, 'error');
    }
  }
});

// Header Full Export Buttons (Honoring all active filters)
btnExportExcel.addEventListener('click', async () => {
  btnExportExcel.disabled = true;
  try {
    if (state.activeView === 'databases') {
      const params = new URLSearchParams();
      if (state.environment) params.append('environment', state.environment);
      if (state.search) params.append('search', state.search);
      if (state.cluster) params.append('cluster', state.cluster);
      if (state.sqlServer) params.append('sqlServer', state.sqlServer);
      if (state.platform) params.append('platform', state.platform);

      showToast('Формирование файла Excel по фильтрам...', 'info');
      const res = await fetch(`/api/export/excel?${params.toString()}`);
      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_Databases_Filtered_${formatDate(new Date())}.xls`);
        showToast('Выгрузка баз 1С в Excel успешно завершена', 'success');
      } else {
        showToast(`Ошибка экспорта в Excel (${res.status})`, 'error');
      }
    } else if (state.activeView === 'files') {
      const params = new URLSearchParams();
      if (state.filesEnvironment) params.append('environment', state.filesEnvironment);
      if (state.filesStatus) params.append('status', state.filesStatus);
      if (state.filesSearch) params.append('search', state.filesSearch);
      if (state.filesCluster) params.append('cluster', state.filesCluster);
      if (state.filesSqlServer) params.append('sqlServer', state.filesSqlServer);

      showToast('Формирование файла Excel по фильтрам...', 'info');
      const res = await fetch(`/api/export/files/excel?${params.toString()}`);
      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_DBMS_Files_Filtered_${formatDate(new Date())}.xls`);
        showToast('Выгрузка файлов СУБД в Excel успешно завершена', 'success');
      } else {
        showToast(`Ошибка экспорта файлов в Excel (${res.status})`, 'error');
      }
    } else if (state.activeView === 'services') {
      const items = filterServices();
      if (items.length === 0) {
        showToast('Нет данных служб 1С для экспорта', 'warning');
        return;
      }
      showToast('Экспорт служб 1С в Excel (CSV)...', 'info');
      const headers = ['№', 'Среда', 'Сервер', 'Порт', 'Служба 1С', 'Статус', 'Пользователь', 'Каталог кластера', 'Порт RAS'];
      const rows = items.map((s, i) => [
        i + 1,
        s.environment || '',
        s.host || '',
        s.clusterPort || '',
        s.displayName || s.serviceName || '',
        s.status || '',
        s.startName || '',
        s.clusterDir || '',
        (s.rasPort && s.rasPort > 0) ? s.rasPort : ''
      ]);
      downloadCsv(headers, rows, `1C_Services_${formatDate(new Date())}.csv`);
      showToast('Выгрузка служб 1С успешно завершена', 'success');
    } else if (state.activeView === 'audit') {
      const items = filterAudit();
      if (items.length === 0) {
        showToast('Журнал аудита пуст по выбранным критериям', 'warning');
        return;
      }
      showToast('Экспорт журнала аудита в Excel (CSV)...', 'info');
      const headers = ['№', 'Дата и время', 'IP клиента', 'Сервер:Порт', 'Служба 1С', 'Действие', 'Статус', 'Время (с)', 'Результат / Ошибка'];
      const rows = items.map((e, i) => {
        const portStr = (e.clusterPort && e.clusterPort > 0)
          ? `${e.host}:${e.clusterPort}`
          : ((e.rasPort && e.rasPort > 0) ? `${e.host}:${e.rasPort}` : '—');
        const durationSec = ((Number(e.durationMs) || 0) / 1000).toFixed(2);
        return [
          i + 1,
          e.timestampLocal || '',
          e.clientIp || '',
          portStr,
          e.displayName || e.serviceName || '',
          e.action || '',
          e.status || '',
          durationSec,
          e.status === 'SUCCESS' ? 'Операция выполнена успешно' : (e.errorMessage || '')
        ];
      });
      downloadCsv(headers, rows, `1C_Audit_${formatDate(new Date())}.csv`);
      showToast('Выгрузка журнала аудита успешно завершена', 'success');
    }
  } catch (err) {
    showToast(`Ошибка экспорта: ${err.message}`, 'error');
  } finally {
    btnExportExcel.disabled = false;
  }
});

btnExportJson.addEventListener('click', async () => {
  btnExportJson.disabled = true;
  try {
    if (state.activeView === 'databases') {
      const params = new URLSearchParams();
      if (state.environment) params.append('environment', state.environment);
      if (state.search) params.append('search', state.search);
      if (state.cluster) params.append('cluster', state.cluster);
      if (state.sqlServer) params.append('sqlServer', state.sqlServer);
      if (state.platform) params.append('platform', state.platform);

      showToast('Формирование файла JSON по фильтрам...', 'info');
      const res = await fetch(`/api/export/json?${params.toString()}`);
      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_Databases_Filtered_${formatDate(new Date())}.json`);
        showToast('Выгрузка баз 1С в JSON успешно завершена', 'success');
      } else {
        showToast(`Ошибка экспорта в JSON (${res.status})`, 'error');
      }
    } else if (state.activeView === 'files') {
      const params = new URLSearchParams();
      if (state.filesEnvironment) params.append('environment', state.filesEnvironment);
      if (state.filesStatus) params.append('status', state.filesStatus);
      if (state.filesSearch) params.append('search', state.filesSearch);
      if (state.filesCluster) params.append('cluster', state.filesCluster);
      if (state.filesSqlServer) params.append('sqlServer', state.filesSqlServer);

      showToast('Формирование файла JSON по фильтрам...', 'info');
      const res = await fetch(`/api/export/files/json?${params.toString()}`);
      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_DBMS_Files_Filtered_${formatDate(new Date())}.json`);
        showToast('Выгрузка файлов СУБД в JSON успешно завершена', 'success');
      } else {
        showToast(`Ошибка экспорта файлов в JSON (${res.status})`, 'error');
      }
    } else if (state.activeView === 'services') {
      const items = filterServices();
      if (items.length === 0) {
        showToast('Нет данных служб 1С для экспорта', 'warning');
        return;
      }
      downloadJson(items, `1C_Services_${formatDate(new Date())}.json`);
      showToast('Выгрузка служб 1С в JSON успешно завершена', 'success');
    } else if (state.activeView === 'audit') {
      const items = filterAudit();
      if (items.length === 0) {
        showToast('Журнал аудита пуст по выбранным критериям', 'warning');
        return;
      }
      downloadJson(items, `1C_Audit_${formatDate(new Date())}.json`);
      showToast('Выгрузка журнала аудита в JSON успешно завершена', 'success');
    }
  } catch (err) {
    showToast(`Ошибка экспорта: ${err.message}`, 'error');
  } finally {
    btnExportJson.disabled = false;
  }
});

function downloadBlob(blob, filename) {
  const url = window.URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  window.URL.revokeObjectURL(url);
}

function downloadCsv(headers, rows, filename) {
  const escapeCsv = (val) => `"${String(val ?? '').replace(/"/g, '""')}"`;
  const csvContent = '\uFEFF' + [
    headers.map(escapeCsv).join(';'),
    ...rows.map(r => r.map(escapeCsv).join(';'))
  ].join('\r\n');
  const blob = new Blob([csvContent], { type: 'text/csv;charset=utf-8;' });
  downloadBlob(blob, filename);
}

function downloadJson(data, filename) {
  const blob = new Blob([JSON.stringify(data, null, 2)], { type: 'application/json;charset=utf-8;' });
  downloadBlob(blob, filename);
}

function formatDate(d) {
  return d.toISOString().replace(/[-:T]/g, '').slice(0, 15);
}

let isColumnResizing = false;

function makeTableResizable(table) {
  if (!table) return;
  const thList = table.querySelectorAll('thead th');
  thList.forEach((th) => {
    if (th.querySelector('.row-checkbox') || th.querySelector('.col-resizer')) return;

    const resizer = document.createElement('div');
    resizer.className = 'col-resizer';
    th.appendChild(resizer);

    let startX = 0;
    let startWidth = 0;

    resizer.addEventListener('mousedown', (e) => {
      e.stopPropagation();
      e.preventDefault();
      isColumnResizing = true;
      startX = e.pageX;
      startWidth = th.offsetWidth;
      resizer.classList.add('resizing');
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';

      function onMouseMove(e) {
        if (!isColumnResizing) return;
        const diff = e.pageX - startX;
        const newWidth = Math.max(35, startWidth + diff);
        th.style.width = newWidth + 'px';
        th.style.minWidth = newWidth + 'px';
      }

      function onMouseUp() {
        if (isColumnResizing) {
          setTimeout(() => {
            isColumnResizing = false;
          }, 60);
          resizer.classList.remove('resizing');
          document.body.style.cursor = '';
          document.body.style.userSelect = '';
        }
        document.removeEventListener('mousemove', onMouseMove);
        document.removeEventListener('mouseup', onMouseUp);
      }

      document.addEventListener('mousemove', onMouseMove);
      document.addEventListener('mouseup', onMouseUp);
    });
  });
}

// Sorting Event Handlers for Databases
document.querySelectorAll('th.sortable').forEach(th => {
  th.addEventListener('click', () => {
    if (isColumnResizing) return;
    const field = th.dataset.sort;
    if (state.sortBy === field) {
      state.sortDir = state.sortDir === 'asc' ? 'desc' : 'asc';
    } else {
      state.sortBy = field;
      state.sortDir = 'asc';
    }
    state.page = 1;
    loadDatabases();
  });
});

// Sorting Event Handlers for Files
document.querySelectorAll('th.sortable-files').forEach(th => {
  th.addEventListener('click', () => {
    if (isColumnResizing) return;
    const field = th.dataset.sort;
    if (state.filesSortBy === field) {
      state.filesSortDir = state.filesSortDir === 'asc' ? 'desc' : 'asc';
    } else {
      state.filesSortBy = field;
      state.filesSortDir = 'asc';
    }
    state.filesPage = 1;
    loadFiles();
  });
});

// Modal Maximize / Restore Controls
const modalMaximize = document.getElementById('modalMaximize');
const adModalMaximize = document.getElementById('adModalMaximize');

if (modalMaximize) {
  modalMaximize.addEventListener('click', () => {
    detailsModal.querySelector('.modal-content').classList.toggle('maximized');
  });
}

if (adModalMaximize) {
  adModalMaximize.addEventListener('click', () => {
    adGroupModal.querySelector('.modal-content').classList.toggle('maximized');
  });
}

// AD Group Members Popup (Clean Badge, No Emoji, Instant Client Cache)
window.__adGroupCache = window.__adGroupCache || {};

window.openAdGroup = async function(groupName) {
  if (!groupName || groupName === '-' || groupName === 'Отсутствует' || groupName.includes('не удалось найти')) {
    showToast('Имя группы не определено', 'warning');
    return;
  }

  state.currentAdGroupName = groupName;
  state.currentAdGroupDesc = '';
  state.currentAdGroupMembers = [];

  adModalGroupName.textContent = groupName;
  adMemberSearchInput.value = '';
  adGroupModal.style.display = 'flex';

  // Instant Cache Hit (< 1ms UI response)
  const cached = window.__adGroupCache[groupName];
  if (cached && cached.members && cached.members.length > 0) {
    state.currentAdGroupMembers = cached.members;
    state.currentAdGroupDesc = cached.description || 'Группа безопасности Active Directory';
    adModalGroupDesc.textContent = state.currentAdGroupDesc;
    adModalMemberCount.textContent = `${cached.members.length} участников`;
    adModalLoading.style.display = 'none';
    adModalContent.style.display = 'block';
    renderAdMembers(state.currentAdGroupMembers);
    return;
  }

  adModalMemberCount.textContent = 'Загрузка...';
  adModalGroupDesc.textContent = '';
  adModalLoading.style.display = 'flex';
  adModalLoading.innerHTML = `
    <span class="spinner spinner-lg"></span>
    <span>Запрос состава группы из Active Directory...</span>
  `;
  adModalContent.style.display = 'none';

  try {
    const res = await fetch(`/api/activedirectory/group/${encodeURIComponent(groupName)}/members`);
    if (res.ok) {
      const data = await res.json();
      const usersOnly = (data.members || []).filter(m => !m.isGroup);
      data.members = usersOnly;
      window.__adGroupCache[groupName] = data;
      state.currentAdGroupMembers = usersOnly;
      state.currentAdGroupDesc = data.description || 'Группа безопасности Active Directory';
      adModalGroupDesc.textContent = state.currentAdGroupDesc;
      adModalMemberCount.textContent = `${usersOnly.length} участников`;
      adModalLoading.style.display = 'none';
      adModalContent.style.display = 'block';
      renderAdMembers(state.currentAdGroupMembers);
    } else {
      const err = await res.json().catch(() => ({}));
      adModalLoading.style.display = 'flex';
      adModalLoading.innerHTML = `<span style="color: var(--color-signal-orange); padding: 20px;">${escapeHtml(err.error || 'Не удалось получить состав группы из Active Directory')}</span>`;
      adModalMemberCount.textContent = '0 участников';
    }
  } catch (err) {
    adModalLoading.style.display = 'flex';
    adModalLoading.innerHTML = `<span style="color: var(--color-signal-orange); padding: 20px;">Ошибка обращения к AD: ${escapeHtml(err.message)}</span>`;
  }
};

function renderAdMembers(members) {
  if (!members || members.length === 0) {
    adMembersTableBody.innerHTML = `<tr><td colspan="7" style="text-align: center; color: #8a8380; padding: 25px 10px;">Участники не найдены</td></tr>`;
    return;
  }

  let html = '';
  members.forEach((m, idx) => {
    const isGroup = m.isGroup === true;
    const isEnabled = m.enabled !== false;
    const statusBadge = isGroup
      ? `<span class="badge badge-neutral">Группа</span>`
      : (isEnabled ? `<span class="badge badge-ok">Активен</span>` : `<span class="badge badge-missing">Отключен</span>`);

    const name = m.displayName || m.samAccountName || '—';
    const sam = m.samAccountName || '—';
    const title = m.title || '—';
    const dept = m.department || '—';
    const mail = m.email || '—';

    html += `
      <tr style="border-bottom: 1px solid #201e1d; height: 28px;">
        <td style="padding: 5px 8px; color: #8a8380; text-align: center; font-family: monospace;">${idx + 1}</td>
        <td style="padding: 5px 8px;"><strong style="color: #eeeeee; font-weight: 500;">${escapeHtml(name)}</strong></td>
        <td style="padding: 5px 8px;"><code style="color: #b8b3b0; font-family: monospace;">${escapeHtml(sam)}</code></td>
        <td style="padding: 5px 8px; color: #b8b3b0;" title="${escapeHtml(title)}">${escapeHtml(title)}</td>
        <td style="padding: 5px 8px; color: #b8b3b0;" title="${escapeHtml(dept)}">${escapeHtml(dept)}</td>
        <td style="padding: 5px 8px; color: #b8b3b0;" title="${escapeHtml(mail)}">${escapeHtml(mail)}</td>
        <td style="padding: 5px 8px; text-align: center;">${statusBadge}</td>
      </tr>
    `;
  });

  adMembersTableBody.innerHTML = html;
}

adMemberSearchInput.addEventListener('input', (e) => {
  const query = e.target.value.toLowerCase().trim();
  if (!query) {
    renderAdMembers(state.currentAdGroupMembers);
    return;
  }

  const filtered = state.currentAdGroupMembers.filter(m =>
    (m.displayName && m.displayName.toLowerCase().includes(query)) ||
    (m.samAccountName && m.samAccountName.toLowerCase().includes(query)) ||
    (m.title && m.title.toLowerCase().includes(query)) ||
    (m.department && m.department.toLowerCase().includes(query)) ||
    (m.email && m.email.toLowerCase().includes(query))
  );

  renderAdMembers(filtered);
});

// Modal 2: Export AD Group Members to Excel
const btnExportAdMembersExcel = document.getElementById('btnExportAdMembersExcel');
if (btnExportAdMembersExcel) {
  btnExportAdMembersExcel.addEventListener('click', async () => {
    if (!state.currentAdGroupName || !state.currentAdGroupMembers || state.currentAdGroupMembers.length === 0) {
      showToast('Нет участников для экспорта', 'error');
      return;
    }

    try {
      const res = await fetch('/api/export/adgroup/excel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          groupName: state.currentAdGroupName,
          description: state.currentAdGroupDesc || '',
          members: state.currentAdGroupMembers
        })
      });

      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `AD_Group_${state.currentAdGroupName}_${formatDate(new Date())}.xls`);
        showToast(`Участники группы ${state.currentAdGroupName} (${state.currentAdGroupMembers.length}) выгружены в Excel`, 'success');
      } else {
        showToast('Ошибка формирования Excel-файла участников группы AD', 'error');
      }
    } catch (err) {
      showToast(`Ошибка экспорта: ${err.message}`, 'error');
    }
  });
}

adModalClose.addEventListener('click', () => { adGroupModal.style.display = 'none'; });
window.addEventListener('click', (e) => {
  if (e.target === adGroupModal) adGroupModal.style.display = 'none';
  if (e.target === detailsModal) detailsModal.style.display = 'none';
});

// Open Details Modal & Trigger DBMS Deep Inspection
window.openDetails = async function(environment, cluster, name) {
  const item = state.items.find(i => i.environment === environment && i.cluster === cluster && i.name === name);
  if (!item) return;
  state.selectedItem = item;
  state.currentDetails = null;

  modalTitle.textContent = `${item.name} (${item.environment})`;
  modalSubtitle.textContent = `Кластер: ${item.cluster} | СУБД: ${item.sql} [${item.sqlDbName}]`;

  // Render Tab 3: Cluster
  clusterInfoGrid.innerHTML = `
    <div class="info-label">Имя базы:</div><div class="info-value"><strong>${escapeHtml(item.name)}</strong></div>
    <div class="info-label">Описание:</div><div class="info-value">${escapeHtml(item.description || '—')}</div>
    <div class="info-label">UUID базы в кластере:</div><div class="info-value"><code>${escapeHtml(item.uuid || '—')}</code></div>
    <div class="info-label">Кластер 1С:</div><div class="info-value"><code>${escapeHtml(item.cluster)}</code></div>
    <div class="info-label">IP адрес сервера:</div><div class="info-value">${escapeHtml(item.serverIP)}</div>
    <div class="info-label">UUID кластера:</div><div class="info-value"><code>${escapeHtml(item.clusterUUID || '—')}</code></div>
    <div class="info-label">Версия платформы:</div><div class="info-value"><span class="badge badge-neutral">${escapeHtml(item.platform)}</span></div>
    <div class="info-label">Служба 1С:</div><div class="info-value">${escapeHtml(item.serviceName)}</div>
    <div class="info-label">Пользователь службы:</div><div class="info-value">${escapeHtml(item.serviceUser)}</div>
    <div class="info-label">Каталог кластера (-d):</div><div class="info-value"><code>${escapeHtml(item.clusterPath)}</code></div>
  `;

  // Render Tab 4: Infrastructure & Access
  const raGroupBadge = item.raGroup && item.raGroup !== '—' && !item.raGroup.includes('не удалось')
    ? `<span class="badge badge-ok badge-clickable" onclick="openAdGroup('${escapeHtml(item.raGroup)}')">${escapeHtml(item.raGroup)}</span>`
    : `<span class="badge badge-missing">Не назначена</span>`;

  const oneCGroupBadge = item.oneCGroup && item.oneCGroup !== '—' && !item.oneCGroup.includes('не удалось')
    ? `<span class="badge badge-ok badge-clickable" onclick="openAdGroup('${escapeHtml(item.oneCGroup)}')">${escapeHtml(item.oneCGroup)}</span>`
    : `<span class="badge badge-neutral">Не назначена</span>`;

  infraInfoGrid.innerHTML = `
    <div class="info-label">Группа доступа (AD):</div><div class="info-value"><span class="badge ${item.accessGroup !== 'Отсутствует' ? 'badge-ok badge-clickable' : 'badge-missing'}" onclick="openAdGroup('${escapeHtml(item.accessGroup)}')">${escapeHtml(item.accessGroup)}</span></div>
    <div class="info-label">RA-группа (RDP/RemoteApp):</div><div class="info-value">${raGroupBadge}</div>
    <div class="info-label">1C-группа платформы:</div><div class="info-value">${oneCGroupBadge}</div>
    <div class="info-label">Файл ярлыка v8i:</div><div class="info-value"><code>${escapeHtml(item.v8iFile || '—')}</code></div>
    <div class="info-label">Регистрация в Consul:</div><div class="info-value">${escapeHtml(item.consul)}</div>
  `;

  switchTab('tabDbms');
  detailsModal.style.display = 'flex';

  dbmsLoadingState.style.display = 'flex';
  dbmsLoadingState.innerHTML = `
    <span class="spinner spinner-lg"></span>
    <span>Выполняется глубокая инспекция сервера СУБД (файлы на диске, размеры, права)...</span>
  `;
  dbmsContentState.style.display = 'none';

  try {
    const res = await fetch(`/api/databases/details?environment=${encodeURIComponent(environment)}&cluster=${encodeURIComponent(cluster)}&name=${encodeURIComponent(name)}`);
    if (res.ok) {
      const details = await res.json();
      state.currentDetails = details;
      renderDbmsDetails(details);
    } else {
      const err = await res.json().catch(() => ({}));
      dbmsLoadingState.innerHTML = `<span style="color: var(--color-signal-orange);">${escapeHtml(err.message || err.error || 'Не удалось получить данные от сервера СУБД')}</span>`;
    }
  } catch (err) {
    dbmsLoadingState.innerHTML = `<span style="color: var(--color-signal-orange);">Ошибка опроса СУБД: ${escapeHtml(err.message)}</span>`;
  }
};

// Modal 1: Export Database Details to Excel
const btnExportDetailsExcel = document.getElementById('btnExportDetailsExcel');
if (btnExportDetailsExcel) {
  btnExportDetailsExcel.addEventListener('click', async () => {
    if (!state.selectedItem) return;
    try {
      const res = await fetch('/api/export/details/excel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          database: state.selectedItem,
          dbmsDetails: state.currentDetails
        })
      });

      if (res.ok) {
        const blob = await res.blob();
        downloadBlob(blob, `1C_Details_${state.selectedItem.name}_${formatDate(new Date())}.xls`);
        showToast(`Сведения базы ${state.selectedItem.name} выгружены в Excel`, 'success');
      } else {
        showToast('Ошибка формирования Excel-файла сведений', 'error');
      }
    } catch (err) {
      showToast(`Ошибка экспорта: ${err.message}`, 'error');
    }
  });
}

function renderDbmsDetails(details) {
  dbmsLoadingState.style.display = 'none';
  dbmsContentState.style.display = 'block';

  if (!details || details.error) {
    dbmsSummaryGrid.innerHTML = `<div style="grid-column: span 2; color: var(--color-signal-orange);">⚠️ ${escapeHtml(details?.error || 'Ошибка инспекции СУБД')}</div>`;
    dbmsFilesTableBody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--color-warm-granite);">Файлы недоступны</td></tr>`;
    dbmsUsersTableBody.innerHTML = `<tr><td colspan="4" style="text-align: center; color: var(--color-warm-granite);">Пользователи недоступны</td></tr>`;
    return;
  }

  const createdStr = details.createdDate ? new Date(details.createdDate).toLocaleString('ru-RU') : 'Неизвестно';
  const backupStr = details.lastBackupDate ? new Date(details.lastBackupDate).toLocaleString('ru-RU') : 'Нет сведений';

  dbmsSummaryGrid.innerHTML = `
    <div class="info-label">Сервер СУБД:</div><div class="info-value"><code>${escapeHtml(details.dbServer)}</code></div>
    <div class="info-label">Имя базы в СУБД:</div><div class="info-value"><strong style="font-size: 13px;">${escapeHtml(details.databaseName)}</strong></div>
    <div class="info-label">Тип СУБД:</div><div class="info-value"><span class="badge badge-neutral">${escapeHtml(details.dbmsType)}</span></div>
    <div class="info-label">Общий объем базы:</div><div class="info-value"><strong style="font-size: 15px; color: #101010;">${details.totalSizeGb} GB</strong> <span style="font-size: 11px; color: #555;">(${details.totalSizeMb} MB)</span></div>
    <div class="info-label">Владелец / Создатель:</div><div class="info-value">${escapeHtml(details.owner || '—')}</div>
    <div class="info-label">Дата создания:</div><div class="info-value">${createdStr}</div>
    <div class="info-label">Состояние базы:</div><div class="info-value"><span class="badge badge-ok">${escapeHtml(details.state || 'ONLINE')}</span></div>
    <div class="info-label">Модель восстановления:</div><div class="info-value">${escapeHtml(details.recoveryModel || '—')}</div>
    <div class="info-label">Колляция:</div><div class="info-value"><code>${escapeHtml(details.collation || '—')}</code></div>
    <div class="info-label">Последний бэкап:</div><div class="info-value">${backupStr}</div>
  `;

  if (details.files && details.files.length > 0) {
    let filesHtml = '';
    details.files.forEach(f => {
      filesHtml += `
        <tr>
          <td><strong style="color: var(--color-bone); font-weight: 500;">${escapeHtml(f.fileName)}</strong></td>
          <td><span class="badge badge-neutral">${escapeHtml(f.fileType)}</span></td>
          <td class="mono">${f.sizeMb}</td>
          <td><strong class="mono" style="color: var(--color-metric-green);">${f.sizeGb}</strong></td>
          <td><code style="font-size: 10.5px;">${escapeHtml(f.physicalPath)}</code></td>
        </tr>
      `;
    });
    dbmsFilesTableBody.innerHTML = filesHtml;
  } else {
    dbmsFilesTableBody.innerHTML = `<tr><td colspan="5" style="text-align: center; color: var(--color-warm-granite);">Файлы не найдены</td></tr>`;
  }

  if (details.permissions && details.permissions.length > 0) {
    let usersHtml = '';
    details.permissions.forEach(u => {
      usersHtml += `
        <tr>
          <td><strong style="color: var(--color-bone); font-weight: 500;">${escapeHtml(u.principalName)}</strong></td>
          <td><span class="badge badge-neutral">${escapeHtml(u.principalType)}</span></td>
          <td><span class="badge ${u.roleOrPermission === 'db_owner' ? 'badge-ok' : 'badge-neutral'}">${escapeHtml(u.roleOrPermission)}</span></td>
          <td class="mono">${escapeHtml(u.state)}</td>
        </tr>
      `;
    });
    dbmsUsersTableBody.innerHTML = usersHtml;
  } else {
    dbmsUsersTableBody.innerHTML = `<tr><td colspan="4" style="text-align: center; color: var(--color-warm-granite);">Нет сведений о пользователях базы данных</td></tr>`;
  }
}

function switchTab(tabId) {
  document.querySelectorAll('.tab-btn').forEach(btn => {
    btn.classList.toggle('active', btn.dataset.tab === tabId);
  });
  document.querySelectorAll('.tab-pane').forEach(pane => {
    pane.classList.toggle('active', pane.id === tabId);
  });
}

document.querySelectorAll('.tab-btn').forEach(btn => {
  btn.addEventListener('click', () => switchTab(btn.dataset.tab));
});

modalClose.addEventListener('click', () => { detailsModal.style.display = 'none'; });

// Scan confirmation modal elements
const scanConfirmModal = document.getElementById('scanConfirmModal');
const scanConfirmClose = document.getElementById('scanConfirmClose');
const btnCancelScanConfirm = document.getElementById('btnCancelScanConfirm');
const btnProceedScanConfirm = document.getElementById('btnProceedScanConfirm');

// Trigger Manual Rescan with Confirmation Dialog
btnScan.addEventListener('click', (e) => {
  e.preventDefault();
  if (state.isScanning) return;
  if (scanConfirmModal) {
    scanConfirmModal.classList.add('open');
    scanConfirmModal.style.display = 'flex';
  } else {
    executeDatabaseScan();
  }
});

if (scanConfirmClose) {
  scanConfirmClose.addEventListener('click', () => {
    scanConfirmModal.classList.remove('open');
    scanConfirmModal.style.display = 'none';
  });
}
if (btnCancelScanConfirm) {
  btnCancelScanConfirm.addEventListener('click', () => {
    scanConfirmModal.classList.remove('open');
    scanConfirmModal.style.display = 'none';
  });
}
if (scanConfirmModal) {
  scanConfirmModal.addEventListener('click', (e) => {
    if (e.target === scanConfirmModal) {
      scanConfirmModal.classList.remove('open');
      scanConfirmModal.style.display = 'none';
    }
  });
}
if (btnProceedScanConfirm) {
  btnProceedScanConfirm.addEventListener('click', async () => {
    scanConfirmModal.classList.remove('open');
    scanConfirmModal.style.display = 'none';
    if (state.isScanning) return;
    await executeDatabaseScan();
  });
}

async function executeDatabaseScan() {
  state.isScanning = true;
  btnScan.disabled = true;
  btnScan.innerHTML = `<span class="spinner" style="border-top-color: var(--color-metric-green); width: 11px; height: 11px; margin-right: 4px;"></span> Сканирование...`;
  liveStatusPulse.classList.add('pulse-orange');

  // Clear tables and display initial loading animation
  state.items = [];
  state.total = 0;
  state.fileItems = [];
  state.filesTotal = 0;
  if (state.selectedKeys) state.selectedKeys.clear();
  updateSelectionUI();

  if (databasesTableBody) {
    databasesTableBody.innerHTML = `
      <tr>
        <td colspan="11" style="text-align: center; padding: 0;">
          <div class="loading-container">
            <span class="spinner spinner-lg"></span>
            <span>Загрузка информационных баз 1С...</span>
          </div>
        </td>
      </tr>
    `;
  }
  if (filesTableBody) {
    filesTableBody.innerHTML = `
      <tr>
        <td colspan="10" style="text-align: center; padding: 0;">
          <div class="loading-container">
            <span class="spinner spinner-lg"></span>
            <span>Сбор сведений о размерах и файлах баз данных СУБД...</span>
          </div>
        </td>
      </tr>
    `;
  }
  renderPagination();

  try {
    const res = await fetch('/api/sync/scan', { method: 'POST' });
    if (res.ok) {
      showToast('Сканирование кластеров запущено.', 'info');
      
      const initialScanTime = state.lastScanTime;
      let checkCount = 0;

      const pollInterval = setInterval(async () => {
        checkCount++;
        await loadStats(true);

        if ((state.lastScanTime && state.lastScanTime !== initialScanTime) || checkCount > 30) {
          clearInterval(pollInterval);
          state.isScanning = false;
          btnScan.disabled = false;
          btnScan.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="scan-icon"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Обновить базы`;
          liveStatusPulse.classList.remove('pulse-orange');
          await loadDatabases();
          await loadFiles(true);
          await loadFilters();
          showToast('Сканирование завершено! Таблица обновлена.', 'success');
        }
      }, 1500);
    } else {
      const err = await res.json();
      showToast(err.message || 'Ошибка запуска сканирования', 'error');
      state.isScanning = false;
      btnScan.disabled = false;
      btnScan.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="scan-icon"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Обновить базы`;
      loadCurrentView();
    }
  } catch (err) {
    showToast(`Ошибка: ${err.message}`, 'error');
    state.isScanning = false;
    btnScan.disabled = false;
    btnScan.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="scan-icon"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Обновить базы`;
    loadCurrentView();
  }
}

// Event Listeners for Tab 1 Filters (Databases)
let searchTimeout;
searchInput.addEventListener('input', (e) => {
  clearTimeout(searchTimeout);
  searchTimeout = setTimeout(() => {
    state.search = e.target.value;
    state.page = 1;
    loadDatabases();
  }, 250);
});

envSelect.addEventListener('change', (e) => {
  state.environment = e.target.value;
  state.page = 1;
  loadDatabases();
});







pageSizeSelect.addEventListener('change', (e) => {
  state.pageSize = e.target.value === 'ALL' ? 100000 : parseInt(e.target.value);
  state.page = 1;
  loadDatabases();
});

// Event Listeners for Tab 2 Filters (Files)
let filesSearchTimeout;
filesSearchInput.addEventListener('input', (e) => {
  clearTimeout(filesSearchTimeout);
  filesSearchTimeout = setTimeout(() => {
    state.filesSearch = e.target.value;
    state.filesPage = 1;
    loadFiles();
  }, 250);
});



filesEnvSelect.addEventListener('change', (e) => {
  state.filesEnvironment = e.target.value;
  state.filesPage = 1;
  loadFiles();
});





filesPageSizeSelect.addEventListener('change', (e) => {
  state.filesPageSize = e.target.value === 'ALL' ? 100000 : parseInt(e.target.value);
  state.filesPage = 1;
  loadFiles();
});

// Pagination Navigation
btnPrevPage.addEventListener('click', () => {
  if (state.activeView === 'databases') {
    if (state.page > 1) {
      state.page--;
      loadDatabases();
    }
  } else {
    if (state.filesPage > 1) {
      state.filesPage--;
      loadFiles();
    }
  }
});

btnNextPage.addEventListener('click', () => {
  if (state.activeView === 'databases') {
    if (state.page < state.totalPages) {
      state.page++;
      loadDatabases();
    }
  } else {
    if (state.filesPage < state.filesTotalPages) {
      state.filesPage++;
      loadFiles();
    }
  }
});

function escapeHtml(text) {
  if (!text) return '';
  const div = document.createElement('div');
  div.textContent = text;
  return div.innerHTML;
}

// Initial Load & Auto-Refresh
document.addEventListener('DOMContentLoaded', () => {
  loadConfig();
  loadStats();
  loadFilters();
  loadDatabases();
  loadFiles(true); // Параллельная предварительная загрузка данных СУБД в фоне

  ['mainDatabasesTable', 'filesDatabasesTable', 'servicesTable', 'auditTable', 'clusterHealthTable', 'clusterLogsTable'].forEach(id => {
    const el = document.getElementById(id);
    if (el) makeTableResizable(el);
  });

  setInterval(() => {
    if (!state.isScanning) {
      loadStats(true);
    }
  }, 5000);
});


// ==========================================
// Cluster Health & Diagnostics Modal Controller
// ==========================================
const btnOpenClusterHealth = document.getElementById('btnOpenClusterHealth');
const metricCardClusters = document.getElementById('metricCardClusters');
const clusterHealthModal = document.getElementById('clusterHealthModal');
const clusterHealthClose = document.getElementById('clusterHealthClose');
const clusterHealthTableBody = document.getElementById('clusterHealthTableBody');
const clusterHealthSearch = document.getElementById('clusterHealthSearch');
const btnRefreshClusterHealth = document.getElementById('btnRefreshClusterHealth');
const btnExportClusterHealthExcel = document.getElementById('btnExportClusterHealthExcel');
const chTabAll = document.getElementById('chTabAll');
const chTabOnline = document.getElementById('chTabOnline');
const chTabEmpty = document.getElementById('chTabEmpty');
const chTabOffline = document.getElementById('chTabOffline');
const clusterHealthTotalBadge = document.getElementById('clusterHealthTotalBadge');
const chCountAll = document.getElementById('chCountAll');
const chCountOnline = document.getElementById('chCountOnline');
const chCountEmpty = document.getElementById('chCountEmpty');
const chCountOffline = document.getElementById('chCountOffline');

let clusterHealthData = [];
let clusterLogsData = [];
let clusterHealthFilter = 'all';
let clusterHealthSortBy = 'environment';
let clusterHealthSortDir = 'asc';
let clusterLogsSortBy = 'timestamp';
let clusterLogsSortDir = 'desc';

if (btnOpenClusterHealth) {
  btnOpenClusterHealth.addEventListener('click', () => {
    openClusterHealthModal();
  });
}

if (metricCardClusters) {
  metricCardClusters.addEventListener('click', () => {
    openClusterHealthModal();
  });
}

if (clusterHealthClose) {
  clusterHealthClose.addEventListener('click', () => {
    const modal = document.getElementById('clusterHealthModal');
    if (modal) modal.style.display = 'none';
  });
}

if (clusterHealthModal) {
  clusterHealthModal.addEventListener('click', (e) => {
    if (e.target === clusterHealthModal) {
      clusterHealthModal.style.display = 'none';
    }
  });
}

if (btnRefreshClusterHealth) {
  btnRefreshClusterHealth.addEventListener('click', () => {
    loadClusterHealth();
  });
}

if (clusterHealthSearch) {
  clusterHealthSearch.addEventListener('input', () => {
    if (clusterHealthFilter === 'logs') {
      renderClusterLogsTable();
    } else {
      renderClusterHealthTable();
    }
  });
}

document.querySelectorAll('.ch-tab').forEach(tab => {
  tab.addEventListener('click', () => {
    document.querySelectorAll('.ch-tab').forEach(t => t.classList.remove('active'));
    tab.classList.add('active');
    clusterHealthFilter = tab.dataset.filter || 'all';

    const chTable = document.getElementById('clusterHealthTable');
    const logsTable = document.getElementById('clusterLogsTable');

    if (clusterHealthFilter === 'logs') {
      if (chTable) chTable.style.display = 'none';
      if (logsTable) {
        logsTable.style.display = 'table';
        makeTableResizable(logsTable);
      }
      renderClusterLogsTable();
    } else {
      if (chTable) {
        chTable.style.display = 'table';
        makeTableResizable(chTable);
      }
      if (logsTable) logsTable.style.display = 'none';
      renderClusterHealthTable();
    }
  });
});

async function openClusterHealthModal() {
  const modal = document.getElementById('clusterHealthModal');
  if (!modal) return;
  modal.style.display = 'flex';
  makeTableResizable(document.getElementById('clusterHealthTable'));
  makeTableResizable(document.getElementById('clusterLogsTable'));
  await loadClusterHealth();
}

async function loadClusterHealth() {
  const tbody = document.getElementById('clusterHealthTableBody');
  if (!tbody) return;

  tbody.innerHTML = `
    <tr>
      <td colspan="10" style="text-align: center; padding: 30px;">
        <div class="loading-container">
          <span class="spinner spinner-lg"></span>
          <span>Опрос состояния кластеров 1С...</span>
        </div>
      </td>
    </tr>
  `;

  try {
    const res = await fetch('/api/databases/clusters/health');
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    clusterHealthData = data.clusters || [];
    clusterLogsData = data.logs || [];

    const btnCount = document.getElementById('btnClusterHealthCount');
    if (btnCount && data.total) {
      btnCount.textContent = `(${data.total})`;
    }

    const badge = document.getElementById('clusterHealthTotalBadge');
    if (badge) badge.textContent = `${data.total} кластеров`;

    const cAll = document.getElementById('chCountAll');
    if (cAll) cAll.textContent = data.total || 0;

    const cOnline = document.getElementById('chCountOnline');
    if (cOnline) cOnline.textContent = data.online || 0;

    const cEmpty = document.getElementById('chCountEmpty');
    if (cEmpty) cEmpty.textContent = data.empty || 0;

    const cOffline = document.getElementById('chCountOffline');
    if (cOffline) cOffline.textContent = (data.offline || 0) + (data.errors || 0);

    const cLogs = document.getElementById('chCountLogs');
    if (cLogs) cLogs.textContent = clusterLogsData.length || 0;

    if (clusterHealthFilter === 'logs') {
      renderClusterLogsTable();
    } else {
      renderClusterHealthTable();
    }
  } catch (err) {
    tbody.innerHTML = `
      <tr>
        <td colspan="10" style="text-align: center; padding: 25px; color: var(--color-signal-orange);">
          Не удалось загрузить диагностику кластеров: ${escapeHtml(err.message)}
        </td>
      </tr>
    `;
  }
}

function updateClusterHealthSortHeaders() {
  document.querySelectorAll('th.sortable-cluster-health').forEach(th => {
    const field = th.dataset.sort;
    const icon = th.querySelector('.sort-icon');
    if (field === clusterHealthSortBy) {
      th.classList.add('sorted');
      if (icon) icon.textContent = clusterHealthSortDir === 'asc' ? '▲' : '▼';
    } else {
      th.classList.remove('sorted');
      if (icon) icon.textContent = '⇅';
    }
  });
}

function updateClusterLogsSortHeaders() {
  document.querySelectorAll('th.sortable-cluster-logs').forEach(th => {
    const field = th.dataset.sort;
    const icon = th.querySelector('.sort-icon');
    if (field === clusterLogsSortBy) {
      th.classList.add('sorted');
      if (icon) icon.textContent = clusterLogsSortDir === 'asc' ? '▲' : '▼';
    } else {
      th.classList.remove('sorted');
      if (icon) icon.textContent = '⇅';
    }
  });
}

function renderClusterHealthTable() {
  const tbody = document.getElementById('clusterHealthTableBody');
  if (!tbody) return;
  const search = (clusterHealthSearch ? clusterHealthSearch.value : '').trim().toLowerCase();

  let filtered = clusterHealthData.filter(item => {
    if (clusterHealthFilter === 'online' && item.status !== 'Online') return false;
    if (clusterHealthFilter === 'empty' && item.status !== 'Empty') return false;
    if (clusterHealthFilter === 'offline' && item.status !== 'Offline' && item.status !== 'Error' && item.status !== 'AuthError') return false;

    if (search) {
      const match = (item.server && item.server.toLowerCase().includes(search)) ||
                    (item.rasAddress && item.rasAddress.toLowerCase().includes(search)) ||
                    (item.platformVersion && item.platformVersion.toLowerCase().includes(search)) ||
                    (item.errorMessage && item.errorMessage.toLowerCase().includes(search)) ||
                    (item.cimStatus && item.cimStatus.toLowerCase().includes(search));
      if (!match) return false;
    }
    return true;
  });

  updateClusterHealthSortHeaders();

  if (filtered.length === 0) {
    tbody.innerHTML = `
      <tr>
        <td colspan="8" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">
          Кластеры по выбранным критериям не найдены
        </td>
      </tr>
    `;
    return;
  }

  filtered.sort((a, b) => {
    let valA = a[clusterHealthSortBy] ?? '';
    let valB = b[clusterHealthSortBy] ?? '';

    if (clusterHealthSortBy === 'environment') {
      const order = { 'PROD': 1, 'DEV': 2 };
      valA = order[valA] ?? 99;
      valB = order[valB] ?? 99;
      if (valA !== valB) return clusterHealthSortDir === 'asc' ? valA - valB : valB - valA;
      return (a.server || '').localeCompare(b.server || '', 'ru', { numeric: true });
    }

    if (clusterHealthSortBy === 'status') {
      const order = { 'Online': 1, 'Empty': 2, 'AuthError': 3, 'Offline': 4, 'Error': 5 };
      valA = order[valA] ?? 99;
      valB = order[valB] ?? 99;
      if (valA !== valB) return clusterHealthSortDir === 'asc' ? valA - valB : valB - valA;
      return (a.server || '').localeCompare(b.server || '', 'ru', { numeric: true });
    }

    valA = String(valA).toLowerCase();
    valB = String(valB).toLowerCase();
    const cmp = valA.localeCompare(valB, 'ru', { numeric: true });
    return clusterHealthSortDir === 'asc' ? cmp : -cmp;
  });

  let html = '';
  filtered.forEach((c, idx) => {
    let statusBadge = '';
    let diagHtml = '';

    if (c.status === 'Online') {
      statusBadge = `<span class="status-text-running">Онлайн</span>`;
      diagHtml = `<span style="color: var(--color-pale-stone); font-size: 11.5px;">ОК. Баз 1С на кластере: ${c.databasesCount}</span>`;
    } else if (c.status === 'Empty') {
      statusBadge = `<span class="status-text-running">Онлайн</span> <span style="color: var(--color-warm-granite); font-size: 10px;">(без баз)</span>`;
      diagHtml = `<span style="color: var(--color-warm-granite); font-size: 11.5px;">ОК. Баз 1С на кластере: 0</span>`;
    } else if (c.status === 'AuthError') {
      statusBadge = `<span class="status-text-stopped">Доступ</span>`;
      diagHtml = `<span style="color: var(--color-warm-granite); font-size: 11.5px;">${escapeHtml(c.errorMessage || 'Ошибка аутентификации')}</span>`;
    } else {
      statusBadge = `<span class="status-text-stopped">Недоступен</span>`;
      diagHtml = `<span style="color: var(--color-warm-granite); font-size: 11.5px;">${escapeHtml(c.errorMessage || 'Служба недоступна')}</span>`;
    }

    const envBadge = c.environment === 'PROD'
      ? `<span class="badge badge-prod">PROD</span>`
      : `<span class="badge badge-dev">DEV</span>`;

    const wmiHtml = c.cimStatus && c.cimStatus !== '—'
      ? `<span class="mono" style="font-size: 11px; color: var(--color-pale-stone); pointer-events: none;" title="${escapeHtml(c.cimStatus)}">${escapeHtml(c.cimStatus.replace(/^Служба:\s*/, ''))}</span>`
      : `<span style="color: var(--color-warm-granite);">—</span>`;

    const cleanRas = c.rasAddress && c.rasAddress !== '—' && !c.rasAddress.endsWith(':0') ? c.rasAddress : '—';

    html += `
      <tr>
        <td style="text-align: center; font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-warm-granite);">${idx + 1}</td>
        <td style="text-align: center;">${envBadge}</td>
        <td style="font-family: var(--font-geist-mono); font-weight: 600; color: var(--color-bone);">
          ${escapeHtml(c.server)}
        </td>
        <td style="font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-pale-stone);">
          ${escapeHtml(cleanRas)}
        </td>
        <td style="text-align: center; white-space: nowrap;">${statusBadge}</td>
        <td style="text-align: center; font-family: var(--font-geist-mono); font-size: 11px;">
          <span class="badge badge-neutral">${escapeHtml(c.platformVersion || '—')}</span>
        </td>
        <td class="cell-truncate" style="max-width: 170px;">${wmiHtml}</td>
        <td>${diagHtml}</td>
      </tr>
    `;
  });

  tbody.innerHTML = html;
}

function renderClusterLogsTable() {
  const tbody = document.getElementById('clusterLogsTableBody');
  if (!tbody) return;

  updateClusterLogsSortHeaders();

  if (!clusterLogsData || clusterLogsData.length === 0) {
    tbody.innerHTML = `
      <tr>
        <td colspan="7" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">
          Нет зафиксированных ошибок опроса кластеров
        </td>
      </tr>
    `;
    return;
  }

  const search = (clusterHealthSearch ? clusterHealthSearch.value : '').trim().toLowerCase();
  const filtered = clusterLogsData.filter(l => {
    if (!search) return true;
    return (l.server && l.server.toLowerCase().includes(search)) ||
           (l.host && l.host.toLowerCase().includes(search)) ||
           (l.stage && l.stage.toLowerCase().includes(search)) ||
           (l.level && l.level.toLowerCase().includes(search)) ||
           (l.message && l.message.toLowerCase().includes(search)) ||
           (l.details && l.details.toLowerCase().includes(search));
  });

  if (filtered.length === 0) {
    tbody.innerHTML = `
      <tr>
        <td colspan="7" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">
          Ничего не найдено по запросу "${escapeHtml(search)}"
        </td>
      </tr>
    `;
    return;
  }

  filtered.sort((a, b) => {
    let valA = a[clusterLogsSortBy] ?? '';
    let valB = b[clusterLogsSortBy] ?? '';

    if (clusterLogsSortBy === 'timestamp') {
      const parseTs = (str) => {
        if (!str) return 0;
        const p = str.split(/[\s.:]+/);
        if (p.length >= 6) {
          return new Date(p[2], p[1] - 1, p[0], p[3], p[4], p[5]).getTime();
        }
        return new Date(str).getTime() || 0;
      };
      const tA = parseTs(valA);
      const tB = parseTs(valB);
      return clusterLogsSortDir === 'asc' ? tA - tB : tB - tA;
    }

    if (clusterLogsSortBy === 'environment') {
      const order = { 'PROD': 1, 'DEV': 2 };
      valA = order[valA] ?? 99;
      valB = order[valB] ?? 99;
      return clusterLogsSortDir === 'asc' ? valA - valB : valB - valA;
    }

    if (clusterLogsSortBy === 'level') {
      const order = { 'Error': 1, 'Warning': 2, 'Info': 3 };
      valA = order[valA] ?? 99;
      valB = order[valB] ?? 99;
      return clusterLogsSortDir === 'asc' ? valA - valB : valB - valA;
    }

    valA = String(valA).toLowerCase();
    valB = String(valB).toLowerCase();
    const cmp = valA.localeCompare(valB, 'ru', { numeric: true });
    return clusterLogsSortDir === 'asc' ? cmp : -cmp;
  });

  let html = '';
  filtered.forEach((l, idx) => {
    const envBadge = l.environment === 'PROD'
      ? `<span class="badge badge-prod">PROD</span>`
      : (l.environment === 'DEV' ? `<span class="badge badge-dev">DEV</span>` : `<span class="badge badge-neutral">${escapeHtml(l.environment || '—')}</span>`);

    const levelBadge = `<span class="status-text-stopped" style="font-size: 11px;">${escapeHtml(l.level === 'Error' ? 'Ошибка' : (l.level === 'Warning' ? 'Предупреждение' : 'Инфо'))}</span>`;

    const stageBadge = `<span class="badge badge-neutral" style="font-family: var(--font-geist-mono); font-size: 10px;">${escapeHtml(l.stage || '—')}</span>`;

    const serverDisplay = l.server || l.host || '—';
    const timeDisplay = l.timestampLocal || (l.timestamp ? new Date(l.timestamp).toLocaleString('ru-RU') : '—');

    let msgHtml = `<span style="color: var(--color-pale-stone); font-family: var(--font-geist-mono); font-size: 11px;">${escapeHtml(l.message || '—')}</span>`;
    if (l.details) {
      msgHtml += `<div style="font-size: 10px; color: var(--color-warm-granite); margin-top: 3px; font-family: var(--font-geist-mono); word-break: break-all;">${escapeHtml(l.details)}</div>`;
    }

    html += `
      <tr>
        <td style="text-align: center; font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-warm-granite);">${idx + 1}</td>
        <td style="font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-pale-stone); white-space: nowrap;">${timeDisplay}</td>
        <td style="text-align: center;">${envBadge}</td>
        <td style="font-family: var(--font-geist-mono); font-weight: 600; color: var(--color-bone);">
          ${escapeHtml(serverDisplay)}
        </td>
        <td>${stageBadge}</td>
        <td style="text-align: center;">${levelBadge}</td>
        <td>${msgHtml}</td>
      </tr>
    `;
  });

  tbody.innerHTML = html;
}

if (btnExportClusterHealthExcel) {
  btnExportClusterHealthExcel.addEventListener('click', () => {
    if (clusterHealthFilter === 'logs') {
      if (!clusterLogsData || clusterLogsData.length === 0) {
        showToast('Журнал логов опроса пуст', 'warning');
        return;
      }
      const search = (clusterHealthSearch ? clusterHealthSearch.value : '').trim().toLowerCase();
      const filtered = clusterLogsData.filter(l => {
        if (!search) return true;
        return (l.server && l.server.toLowerCase().includes(search)) ||
               (l.host && l.host.toLowerCase().includes(search)) ||
               (l.stage && l.stage.toLowerCase().includes(search)) ||
               (l.level && l.level.toLowerCase().includes(search)) ||
               (l.message && l.message.toLowerCase().includes(search)) ||
               (l.details && l.details.toLowerCase().includes(search));
      });

      if (filtered.length === 0) {
        showToast('Нет логов для экспорта по текущему фильтру', 'warning');
        return;
      }

      let csv = '\uFEFF"№";"Время";"Среда";"Сервер / Кластер";"Этап";"Уровень";"Диагностика / Текст ошибки";"Подробности"\r\n';
      filtered.forEach((l, idx) => {
        const time = (l.timestampLocal || l.timestamp || '').replace(/"/g, '""');
        const env = (l.environment || '').replace(/"/g, '""');
        const srv = (l.server || l.host || '').replace(/"/g, '""');
        const stg = (l.stage || '').replace(/"/g, '""');
        const lvl = (l.level || '').replace(/"/g, '""');
        const msg = (l.message || '').replace(/"/g, '""');
        const dtl = (l.details || '').replace(/"/g, '""');
        csv += `"${idx + 1}";"${time}";"${env}";"${srv}";"${stg}";"${lvl}";"${msg}";"${dtl}"\r\n`;
      });

      const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = `Cluster_Discovery_Logs_${new Date().toISOString().slice(0, 10)}.csv`;
      a.click();
      URL.revokeObjectURL(url);
      showToast(`Экспортировано ${filtered.length} записей логов опроса`, 'success');
      return;
    }

    if (!clusterHealthData || clusterHealthData.length === 0) {
      showToast('Нет данных для экспорта', 'warning');
      return;
    }

    const search = (clusterHealthSearch ? clusterHealthSearch.value : '').trim().toLowerCase();
    const filtered = clusterHealthData.filter(item => {
      if (clusterHealthFilter === 'online' && item.status !== 'Online') return false;
      if (clusterHealthFilter === 'empty' && item.status !== 'Empty') return false;
      if (clusterHealthFilter === 'offline' && item.status !== 'Offline' && item.status !== 'Error' && item.status !== 'AuthError') return false;

      if (search) {
        const match = (item.server && item.server.toLowerCase().includes(search)) ||
                      (item.rasAddress && item.rasAddress.toLowerCase().includes(search)) ||
                      (item.platformVersion && item.platformVersion.toLowerCase().includes(search)) ||
                      (item.errorMessage && item.errorMessage.toLowerCase().includes(search)) ||
                      (item.cimStatus && item.cimStatus.toLowerCase().includes(search));
        if (!match) return false;
      }
      return true;
    });

    if (filtered.length === 0) {
      showToast('Нет строк для экспорта по текущему фильтру', 'warning');
      return;
    }

    let csv = '\uFEFF"№";"Среда";"Кластер";"RAS Агент";"Статус";"Платформа 1С";"Служба WMI";"Диагностика"\r\n';
    filtered.forEach((c, idx) => {
      const statusText = c.status === 'Online'
        ? 'Онлайн'
        : (c.status === 'Empty' ? 'Онлайн (без баз)' : (c.status === 'AuthError' ? 'Ошибка доступа' : 'Недоступен'));
      const diag = c.status === 'Online'
        ? `ОК. Баз 1С на кластере: ${c.databasesCount}`
        : (c.status === 'Empty' ? 'ОК. Баз 1С на кластере: 0' : (c.errorMessage || '—'));
      const wmi = (c.cimStatus || '—').replace(/"/g, '""');
      csv += `"${idx + 1}";"${c.environment}";"${c.server}";"${c.rasAddress}";"${statusText}";"${c.platformVersion || ''}";"${wmi}";"${diag.replace(/"/g, '""')}"\r\n`;
    });

    const filterName = clusterHealthFilter === 'all' ? 'All' : (clusterHealthFilter === 'online' ? 'Online' : (clusterHealthFilter === 'empty' ? 'Empty' : 'Offline'));
    const blob = new Blob([csv], { type: 'text/csv;charset=utf-8;' });
    const url = URL.createObjectURL(blob);
    const a = document.createElement('a');
    a.href = url;
    a.download = `Cluster_Health_${filterName}_${new Date().toISOString().slice(0, 10)}.csv`;
    a.click();
    URL.revokeObjectURL(url);
    showToast(`Экспортировано ${filtered.length} кластеров (вкладка: ${clusterHealthFilter})`, 'success');
  });
}

// ============================================================================
// SECRET ADMIN CONSOLE: 1C Services Management & Audit Log (Easter Eggs)
// ============================================================================

state.servicesList = [];
state.auditList = [];
state.pendingServiceAction = null;

async function sendConsoleAuditEvent(consoleName, action) {
  try {
    await fetch('/api/services/audit/event', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ consoleName, action })
    });
  } catch (err) {
    console.warn('Не удалось записать событие в аудит:', err);
  }
}

// Consoles must ALWAYS be closed and hidden by default on page load!
let servicesUnlocked = false;
let auditUnlocked = false;
try {
  sessionStorage.removeItem('sec_srv_unlocked');
  sessionStorage.removeItem('sec_audit_unlocked');
} catch {}
if (tabBtnServices) tabBtnServices.style.display = 'none';
if (tabBtnAudit) tabBtnAudit.style.display = 'none';

function toggleServicesManagement() {
  servicesUnlocked = !servicesUnlocked;
  if (!servicesUnlocked) {
    if (tabBtnServices) tabBtnServices.style.display = 'none';
    if (state.activeView === 'services') {
      switchView('databases');
    }
    showToast('Консоль управления службами 1С скрыта', 'info');
    sendConsoleAuditEvent('SERVICES', 'CLOSE_SERVICES_CONSOLE');
  } else {
    if (tabBtnServices) tabBtnServices.style.display = 'inline-flex';
    showToast('Инженерная консоль: Управление службами 1С открыта', 'success');
    sendConsoleAuditEvent('SERVICES', 'OPEN_SERVICES_CONSOLE');
  }
}

function toggleAuditLog() {
  auditUnlocked = !auditUnlocked;
  if (!auditUnlocked) {
    if (tabBtnAudit) tabBtnAudit.style.display = 'none';
    if (state.activeView === 'audit') {
      switchView('databases');
    }
    showToast('Журнал аудита скрыт', 'info');
    sendConsoleAuditEvent('AUDIT', 'CLOSE_AUDIT_CONSOLE');
  } else {
    if (tabBtnAudit) tabBtnAudit.style.display = 'inline-flex';
    showToast('Инженерная консоль: Журнал аудита открыт', 'success');
    sendConsoleAuditEvent('AUDIT', 'OPEN_AUDIT_CONSOLE');
  }
}

window.toggleServicesManagement = toggleServicesManagement;
window.toggleAuditLog = toggleAuditLog;

// 1. Mouse Click with Ctrl + Alt (captured at root level for 100% reliability)
document.addEventListener('click', (e) => {
  const isModifierCombo = (e.ctrlKey && e.altKey) || (e.ctrlKey && e.shiftKey) || (e.metaKey && e.altKey);
  if (!isModifierCombo) return;

  // Check Logo Area click
  const logoTarget = e.target.closest('.logo-area') || e.target.closest('#appLogoArea') || e.target.closest('.brand-title') || e.target.closest('.app-icon');
  if (logoTarget) {
    e.preventDefault();
    e.stopPropagation();
    toggleServicesManagement();
    return;
  }

  // Check Version Pill click
  const versionTarget = e.target.closest('.status-pill-version') || e.target.closest('#footerVersionPill') || e.target.closest('.version-tag') || e.target.closest('.build-date');
  if (versionTarget) {
    e.preventDefault();
    e.stopPropagation();
    toggleAuditLog();
    return;
  }
}, true);


// Load Services
async function loadServices(force = false) {
  if (!servicesTableBody) return;
  if (!force && state.servicesList && state.servicesList.length > 0) {
    renderServicesTable();
    return;
  }
  servicesTableBody.innerHTML = `
    <tr>
      <td colspan="10" style="text-align: center; padding: 30px;">
        <div class="loading-container">
          <span class="spinner spinner-lg"></span>
          <span>Опрос служб 1С и RAS на серверах...</span>
        </div>
      </td>
    </tr>
  `;
  try {
    const res = await fetch(force ? '/api/services?force=true' : '/api/services');
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const contentType = res.headers.get('content-type') || '';
    if (!contentType.includes('application/json')) {
      throw new Error('Бэкенд вернул HTML вместо JSON. Убедитесь, что служба запущена со свежей сборкой.');
    }
    const data = await res.json() || [];
    data.sort((a, b) => (a.displayName || '').localeCompare(b.displayName || '', 'ru'));
    state.servicesList = data;
    renderServicesTable();
  } catch (err) {
    servicesTableBody.innerHTML = `
      <tr>
        <td colspan="10" style="text-align: center; padding: 30px; color: var(--color-signal-orange);">
          Ошибка получения списка служб 1С: ${escapeHtml(err.message)}
        </td>
      </tr>
    `;
  }
}

function filterServices() {
  let list = state.servicesList || [];
  const search = (servicesSearchInput ? servicesSearchInput.value : '').trim().toLowerCase();
  const env = servicesEnvSelect ? servicesEnvSelect.value : 'ALL';
  const status = servicesStatusSelect ? servicesStatusSelect.value : 'ALL';

  let filtered = list.filter(s => {
    if (env !== 'ALL' && s.environment !== env) return false;
    if (status !== 'ALL') {
      const isRunning = (s.status || '').toLowerCase() === 'running';
      if (status === 'Running' && !isRunning) return false;
      if (status === 'Stopped' && isRunning) return false;
    }
    if (search) {
      const match = (s.host && s.host.toLowerCase().includes(search)) ||
                    (s.displayName && s.displayName.toLowerCase().includes(search)) ||
                    (s.serviceName && s.serviceName.toLowerCase().includes(search)) ||
                    (s.startName && s.startName.toLowerCase().includes(search)) ||
                    (s.clusterDir && s.clusterDir.toLowerCase().includes(search)) ||
                    (s.clusterPort && s.clusterPort.toString().includes(search)) ||
                    (s.rasPort && s.rasPort.toString().includes(search));
      if (!match) return false;
    }
    return true;
  });

  const sortBy = state.servicesSortBy || 'displayName';
  const sortDir = state.servicesSortDir || 'asc';
  return [...filtered].sort((a, b) => {
    let valA = a[sortBy] ?? '';
    let valB = b[sortBy] ?? '';
    if (sortBy === 'clusterPort' || sortBy === 'rasPort') {
      valA = Number(valA) || 0;
      valB = Number(valB) || 0;
      return sortDir === 'asc' ? valA - valB : valB - valA;
    }
    valA = valA.toString().toLowerCase();
    valB = valB.toString().toLowerCase();
    return sortDir === 'asc' ? valA.localeCompare(valB, 'ru') : valB.localeCompare(valA, 'ru');
  });
}

function updateServicesSortHeaders() {
  document.querySelectorAll('th.sortable-services').forEach(th => {
    const field = th.dataset.sort;
    const icon = th.querySelector('.sort-icon');
    if (field === state.servicesSortBy) {
      th.classList.add('sorted');
      if (icon) icon.textContent = state.servicesSortDir === 'asc' ? '▲' : '▼';
    } else {
      th.classList.remove('sorted');
      if (icon) icon.textContent = '⇅';
    }
  });
}

function renderServicesTable() {
  if (!servicesTableBody) return;
  const list = filterServices();
  if (list.length === 0) {
    servicesTableBody.innerHTML = `
      <tr>
        <td colspan="10" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">
          Службы 1С не найдены по заданному фильтру
        </td>
      </tr>
    `;
    updateServicesSortHeaders();
    return;
  }

  servicesTableBody.innerHTML = list.map((s, idx) => {
    const isRunning = (s.status || '').toLowerCase() === 'running';
    const statusBadge = isRunning
      ? `<span class="status-text-running">Работает</span>`
      : `<span class="status-text-stopped">Остановлена</span>`;

    const envBadge = s.environment === 'PROD'
      ? `<span class="badge badge-prod">PROD</span>`
      : `<span class="badge badge-dev">DEV</span>`;

    const rasInfo = (s.rasPort && s.rasPort > 0)
      ? `<span class="mono" style="font-size: 11px; color: var(--color-bone);">${s.rasPort}</span>`
      : `<span class="mono" style="color: var(--color-warm-granite);">—</span>`;

    const startDisabled = isRunning ? 'disabled' : '';
    const stopDisabled = !isRunning ? 'disabled' : '';
    const restartDisabled = !isRunning ? 'disabled' : '';
    const cacheDisabled = !isRunning ? 'disabled' : '';

    return `
      <tr>
        <td style="text-align: center; font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-warm-granite);">${idx + 1}</td>
        <td style="text-align: center;">${envBadge}</td>
        <td class="mono" style="font-weight: 600; color: var(--color-bone);">${escapeHtml(s.host)}</td>
        <td style="text-align: center;" class="mono">${s.clusterPort}</td>
        <td>
          <div style="font-weight: 400; color: var(--color-bone); font-size: 11.5px; line-height: 1.35;">${escapeHtml(s.displayName || s.serviceName)}</div>
        </td>
        <td style="text-align: center;">${statusBadge}</td>
        <td class="mono" style="font-size: 11px; color: var(--color-pale-stone);">${escapeHtml(s.startName || '—')}</td>
        <td class="mono" style="font-size: 10px; color: var(--color-warm-granite);" title="${escapeHtml(s.clusterDir)}">${escapeHtml(s.clusterDir || '—')}</td>
        <td style="text-align: center;">${rasInfo}</td>
        <td style="text-align: center;">
          <div class="svc-action-bar">
            <button class="btn-svc" ${startDisabled} data-action="start" data-idx="${idx}" title="Запустить службу 1С и RAS">Старт</button>
            <button class="btn-svc" ${stopDisabled} data-action="stop" data-idx="${idx}" title="Остановить службу 1С и RAS">Стоп</button>
            <button class="btn-svc" ${restartDisabled} data-action="restart" data-idx="${idx}" title="Перезапустить службу 1С и RAS">Перезапуск</button>
            <button class="btn-svc" ${cacheDisabled} data-action="restart-clean-cache" data-idx="${idx}" title="Перезапустить службу 1С с безопасной очисткой кэша snccntx*">- Кэш</button>
          </div>
        </td>
      </tr>
    `;
  }).join('');

  updateServicesSortHeaders();

  // Wire action buttons
  servicesTableBody.querySelectorAll('.btn-svc').forEach(btn => {
    btn.addEventListener('click', () => {
      const idx = parseInt(btn.dataset.idx, 10);
      const action = btn.dataset.action;
      const service = list[idx];
      if (!service) return;
      openServiceConfirmModal(service, action);
    });
  });
}

// Confirmation Modal Handler
function openServiceConfirmModal(service, action) {
  state.pendingServiceAction = { service, action };
  const actionLabels = {
    'start': 'ЗАПУСК',
    'stop': 'ОСТАНОВКА',
    'restart': 'ПЕРЕЗАПУСК',
    'restart-clean-cache': 'ПЕРЕЗАПУСК С ОЧИСТКОЙ КЭША'
  };

  if (confirmModalTitle) confirmModalTitle.textContent = `Подтверждение операции: ${actionLabels[action] || action}`;
  if (confirmModalBodyText) {
    confirmModalBodyText.innerHTML = `
      Вы действительно хотите выполнить операцию <strong>${actionLabels[action] || action}</strong> для службы:<br><br>
      • <strong>Служба:</strong> ${escapeHtml(service.displayName || service.serviceName)}<br>
      • <strong>Сервер:</strong> <span class="mono">${escapeHtml(service.host)}</span><br>
      • <strong>Порт кластера:</strong> <span class="mono">${service.clusterPort}</span>
    `;
  }

  if (confirmModalWarning) {
    if (action === 'restart-clean-cache') {
      confirmModalWarning.style.display = 'block';
      confirmModalWarning.innerHTML = `
        ⚠️ <strong>ВНИМАНИЕ!</strong> Будет выполнена остановка службы, удаление каталогов сессионного кэша <code>snccntx*</code> из каталога <code>${escapeHtml(service.clusterDir || 'srvinfo')}</code> и последующий запуск.<br>
        Все текущие активные сеансы пользователей на данном сервере будут принудительно сброшены!
      `;
    } else if (action === 'stop') {
      confirmModalWarning.style.display = 'block';
      confirmModalWarning.innerHTML = `⚠️ <strong>ВНИМАНИЕ!</strong> Остановка службы сделает кластер на порту <strong>${service.clusterPort}</strong> недоступным для пользователей.`;
    } else {
      confirmModalWarning.style.display = 'none';
    }
  }

  if (confirmModalSpinner) confirmModalSpinner.style.display = 'none';
  if (confirmModalButtons) confirmModalButtons.style.display = 'flex';
  if (btnExecuteServiceAction) btnExecuteServiceAction.disabled = false;
  if (serviceConfirmModal) {
    serviceConfirmModal.classList.add('open');
    serviceConfirmModal.style.display = 'flex';
  }
}

if (confirmModalClose) {
  confirmModalClose.addEventListener('click', () => {
    if (serviceConfirmModal) {
      serviceConfirmModal.classList.remove('open');
      serviceConfirmModal.style.display = 'none';
    }
    state.pendingServiceAction = null;
  });
}

if (btnCancelServiceAction) {
  btnCancelServiceAction.addEventListener('click', () => {
    if (serviceConfirmModal) {
      serviceConfirmModal.classList.remove('open');
      serviceConfirmModal.style.display = 'none';
    }
    state.pendingServiceAction = null;
  });
}

if (serviceConfirmModal) {
  serviceConfirmModal.addEventListener('click', (e) => {
    if (e.target === serviceConfirmModal) {
      serviceConfirmModal.classList.remove('open');
      serviceConfirmModal.style.display = 'none';
      state.pendingServiceAction = null;
    }
  });
}

if (btnExecuteServiceAction) {
  btnExecuteServiceAction.addEventListener('click', async () => {
    if (!state.pendingServiceAction) return;
    const { service, action } = state.pendingServiceAction;

    if (confirmModalSpinner) confirmModalSpinner.style.display = 'block';
    if (confirmModalSpinnerText) confirmModalSpinnerText.textContent = 'Выполнение операции над службой на сервере... Пожалуйста, подождите.';
    if (confirmModalButtons) confirmModalButtons.style.display = 'none';

    try {
      const res = await fetch('/api/services/action', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          host: service.host,
          serviceName: service.serviceName,
          action: action,
          clusterPort: service.clusterPort,
          rasServiceName: service.rasServiceName,
          clusterDir: service.clusterDir
        })
      });

      const result = await res.json();
      if (serviceConfirmModal) {
        serviceConfirmModal.classList.remove('open');
        serviceConfirmModal.style.display = 'none';
      }

      if (result.success) {
        showToast(result.message || 'Операция успешно завершена', 'success');
      } else {
        showToast(result.message || 'Ошибка выполнения операции', 'error');
      }

      await loadServices();
    } catch (err) {
      if (serviceConfirmModal) {
        serviceConfirmModal.classList.remove('open');
        serviceConfirmModal.style.display = 'none';
      }
      showToast('Ошибка при вызове операции: ' + err.message, 'error');
    } finally {
      state.pendingServiceAction = null;
    }
  });
}

// Services Search & Filters
if (servicesSearchInput) servicesSearchInput.addEventListener('input', () => renderServicesTable());
if (servicesEnvSelect) servicesEnvSelect.addEventListener('change', () => renderServicesTable());
if (servicesStatusSelect) servicesStatusSelect.addEventListener('change', () => renderServicesTable());
if (btnRefreshServices) btnRefreshServices.addEventListener('click', () => loadServices(true));

// Load Audit Logs
async function loadAuditLogs(force = false) {
  if (!auditTableBody) return;
  if (!force && state.auditList && state.auditList.length > 0) {
    renderAuditTable();
    return;
  }
  auditTableBody.innerHTML = `
    <tr>
      <td colspan="9" style="text-align: center; padding: 30px;">
        <div class="loading-container">
          <span class="spinner spinner-lg"></span>
          <span>Загрузка записей аудита...</span>
        </div>
      </td>
    </tr>
  `;
  try {
    const search = auditSearchInput ? auditSearchInput.value.trim() : '';
    const res = await fetch(`/api/services/audit?limit=300&search=${encodeURIComponent(search)}`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const contentType = res.headers.get('content-type') || '';
    if (!contentType.includes('application/json')) {
      throw new Error('Бэкенд вернул HTML вместо JSON. Убедитесь, что служба запущена со свежей сборкой.');
    }
    state.auditList = await res.json() || [];
    renderAuditTable();
  } catch (err) {
    auditTableBody.innerHTML = `
      <tr>
        <td colspan="9" style="text-align: center; padding: 30px; color: var(--color-signal-orange);">
          Ошибка загрузки журнала аудита: ${escapeHtml(err.message)}
        </td>
      </tr>
    `;
  }
}

function filterAudit() {
  let list = state.auditList || [];
  const search = (auditSearchInput ? auditSearchInput.value : '').trim().toLowerCase();
  let filtered = list;
  if (search) {
    filtered = list.filter(e => {
      return (e.host && e.host.toLowerCase().includes(search)) ||
             (e.clientHostName && e.clientHostName.toLowerCase().includes(search)) ||
             (e.clientIp && e.clientIp.toLowerCase().includes(search)) ||
             (e.displayName && e.displayName.toLowerCase().includes(search)) ||
             (e.serviceName && e.serviceName.toLowerCase().includes(search)) ||
             (e.action && e.action.toLowerCase().includes(search)) ||
             (e.status && e.status.toLowerCase().includes(search)) ||
             (e.errorMessage && e.errorMessage.toLowerCase().includes(search));
    });
  }

  const sortBy = state.auditSortBy || 'timestamp';
  const sortDir = state.auditSortDir || 'desc';
  return [...filtered].sort((a, b) => {
    let valA = a[sortBy] ?? '';
    let valB = b[sortBy] ?? '';
    if (sortBy === 'durationMs' || sortBy === 'clusterPort' || sortBy === 'rasPort') {
      valA = Number(valA) || 0;
      valB = Number(valB) || 0;
      return sortDir === 'asc' ? valA - valB : valB - valA;
    }
    valA = valA.toString().toLowerCase();
    valB = valB.toString().toLowerCase();
    return sortDir === 'asc' ? valA.localeCompare(valB, 'ru') : valB.localeCompare(valA, 'ru');
  });
}

function updateAuditSortHeaders() {
  document.querySelectorAll('th.sortable-audit').forEach(th => {
    const field = th.dataset.sort;
    const icon = th.querySelector('.sort-icon');
    if (field === state.auditSortBy) {
      th.classList.add('sorted');
      if (icon) icon.textContent = state.auditSortDir === 'asc' ? '▲' : '▼';
    } else {
      th.classList.remove('sorted');
      if (icon) icon.textContent = '⇅';
    }
  });
}

function renderAuditTable() {
  if (!auditTableBody) return;
  const list = filterAudit();
  if (list.length === 0) {
    auditTableBody.innerHTML = `
      <tr>
        <td colspan="9" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">
          Записи аудита отсутствуют
        </td>
      </tr>
    `;
    updateAuditSortHeaders();
    return;
  }

  auditTableBody.innerHTML = list.map((e, idx) => {
    const isSuccess = e.status === 'SUCCESS';
    const statusBadge = isSuccess
      ? `<span class="audit-status-success">Успех</span>`
      : `<span class="audit-status-failed">Ошибка</span>`;

    const actionBadge = `<span class="mono" style="font-size: 11px; color: var(--color-bone);">${escapeHtml(e.action)}</span>`;

    const serverPortDisplay = (e.clusterPort && e.clusterPort > 0)
      ? `${escapeHtml(e.host)}:${e.clusterPort}`
      : ((e.rasPort && e.rasPort > 0)
        ? `${escapeHtml(e.host)}:${e.rasPort}`
        : `<span style="color: var(--color-warm-granite);">—</span>`);

    const durationSec = ((Number(e.durationMs) || 0) / 1000).toFixed(2) + ' с';
    const clientTitle = e.clientHostName ? `${escapeHtml(e.clientHostName)} (${escapeHtml(e.clientIp || '')})` : escapeHtml(e.clientIp || '');

    return `
      <tr>
        <td style="text-align: center; font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-warm-granite);">${idx + 1}</td>
        <td class="mono" style="font-size: 11px; color: var(--color-pale-stone); white-space: nowrap;">${escapeHtml(e.timestampLocal || '-')}</td>
        <td class="mono" style="font-size: 11px; color: var(--color-bone);" title="${clientTitle}">${escapeHtml(e.clientIp || '-')}</td>
        <td class="mono" style="font-weight: 600; color: var(--color-bone);">${serverPortDisplay}</td>
        <td>
          <div style="font-size: 12px; color: var(--color-bone);">${escapeHtml(e.displayName || e.serviceName)}</div>
        </td>
        <td style="text-align: center;">${actionBadge}</td>
        <td style="text-align: center;">${statusBadge}</td>
        <td style="text-align: right;" class="mono" style="font-size: 11px;">${durationSec}</td>
        <td style="font-size: 11px; color: ${isSuccess ? 'var(--color-pale-stone)' : 'var(--color-signal-orange)'};">
          ${escapeHtml(isSuccess ? 'Операция выполнена успешно' : e.errorMessage)}
        </td>
      </tr>
    `;
  }).join('');

  updateAuditSortHeaders();
}

if (auditSearchInput) auditSearchInput.addEventListener('input', () => renderAuditTable());
if (btnRefreshAudit) btnRefreshAudit.addEventListener('click', () => loadAuditLogs(true));

// Sorting Click Handlers for Services, Audit, Cluster Health & Cluster Logs
document.querySelectorAll('th.sortable-services').forEach(th => {
  th.addEventListener('click', () => {
    if (isColumnResizing) return;
    const field = th.dataset.sort;
    if (state.servicesSortBy === field) {
      state.servicesSortDir = state.servicesSortDir === 'asc' ? 'desc' : 'asc';
    } else {
      state.servicesSortBy = field;
      state.servicesSortDir = 'asc';
    }
    renderServicesTable();
  });
});

document.querySelectorAll('th.sortable-audit').forEach(th => {
  th.addEventListener('click', () => {
    if (isColumnResizing) return;
    const field = th.dataset.sort;
    if (state.auditSortBy === field) {
      state.auditSortDir = state.auditSortDir === 'asc' ? 'desc' : 'asc';
    } else {
      state.auditSortBy = field;
      state.auditSortDir = 'desc';
    }
    renderAuditTable();
  });
});

document.querySelectorAll('th.sortable-cluster-health').forEach(th => {
  th.addEventListener('click', () => {
    if (isColumnResizing) return;
    const field = th.dataset.sort;
    if (clusterHealthSortBy === field) {
      clusterHealthSortDir = clusterHealthSortDir === 'asc' ? 'desc' : 'asc';
    } else {
      clusterHealthSortBy = field;
      clusterHealthSortDir = 'asc';
    }
    renderClusterHealthTable();
  });
});

document.querySelectorAll('th.sortable-cluster-logs').forEach(th => {
  th.addEventListener('click', () => {
    if (isColumnResizing) return;
    const field = th.dataset.sort;
    if (clusterLogsSortBy === field) {
      clusterLogsSortDir = clusterLogsSortDir === 'asc' ? 'desc' : 'asc';
    } else {
      clusterLogsSortBy = field;
      clusterLogsSortDir = field === 'timestamp' ? 'desc' : 'asc';
    }
    renderClusterLogsTable();
  });
});

