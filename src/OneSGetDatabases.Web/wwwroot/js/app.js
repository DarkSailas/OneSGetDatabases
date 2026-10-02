
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

  // Tab 5 (Restore) state
  restoreSearch: '',
  restoreSortBy: 'cluster',
  restoreSortDir: 'asc',
  restoreSources: [],
  activeRestoreOperations: new Map(),

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
const tabBtnRestore = document.getElementById('tabBtnRestore');
const toolbarDatabases = document.getElementById('toolbarDatabases');
const toolbarFiles = document.getElementById('toolbarFiles');
const toolbarServices = document.getElementById('toolbarServices');
const toolbarAudit = document.getElementById('toolbarAudit');
const toolbarRestore = document.getElementById('toolbarRestore');
const databasesTableView = document.getElementById('databasesTableView');
const filesTableView = document.getElementById('filesTableView');
const servicesTableView = document.getElementById('servicesTableView');
const auditTableView = document.getElementById('auditTableView');
const restoreTableView = document.getElementById('restoreTableView');
const databasesTableBody = document.getElementById('databasesTableBody');
const filesTableBody = document.getElementById('filesTableBody');
const servicesTableBody = document.getElementById('servicesTableBody');
const auditTableBody = document.getElementById('auditTableBody');
const restoreTableBody = document.getElementById('restoreTableBody');
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

// Secret Restore Elements
const restoreSearchInput = document.getElementById('restoreSearchInput');
const btnRefreshRestore = document.getElementById('btnRefreshRestore');
const restoreModal = document.getElementById('restoreModal');
const restoreModalClose = document.getElementById('restoreModalClose');
const btnCancelRestore = document.getElementById('btnCancelRestore');
const btnStartRestore = document.getElementById('btnStartRestore');
const restoreTargetDbName = document.getElementById('restoreTargetDbName');
const restoreTargetServer = document.getElementById('restoreTargetServer');
const restoreTargetCluster = document.getElementById('restoreTargetCluster');
const restoreSourceSelect = document.getElementById('restoreSourceSelect');
const restoreTimelineLoading = document.getElementById('restoreTimelineLoading');
const restoreTimelineControls = document.getElementById('restoreTimelineControls');
const restoreDateSelect = document.getElementById('restoreDateSelect');
const restorePointSelect = document.getElementById('restorePointSelect');
const restorePointEmpty = document.getElementById('restorePointEmpty');
const chkRestoreDenyJobs = document.getElementById('chkRestoreDenyJobs');
const chkRestoreSimpleShrink = document.getElementById('chkRestoreSimpleShrink');
const chkRestorePermissions = document.getElementById('chkRestorePermissions');

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
const adAddMemberBar = document.getElementById('adAddMemberBar');
const adAddMemberInput = document.getElementById('adAddMemberInput');
const adAddMemberSuggest = document.getElementById('adAddMemberSuggest');
const btnAdAddMember = document.getElementById('btnAdAddMember');
const adMembersActionsHeader = document.getElementById('adMembersActionsHeader');
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
  [tabBtnDatabases, tabBtnFiles, tabBtnServices, tabBtnAudit, tabBtnRestore].forEach(b => {
    if (b) b.classList.toggle('active', b.dataset.view === viewName);
  });
  if (toolbarDatabases) toolbarDatabases.style.display = viewName === 'databases' ? 'flex' : 'none';
  if (toolbarFiles) toolbarFiles.style.display = viewName === 'files' ? 'flex' : 'none';
  if (toolbarServices) toolbarServices.style.display = viewName === 'services' ? 'flex' : 'none';
  if (toolbarAudit) toolbarAudit.style.display = viewName === 'audit' ? 'flex' : 'none';
  if (toolbarRestore) toolbarRestore.style.display = viewName === 'restore' ? 'flex' : 'none';

  if (databasesTableView) databasesTableView.style.display = viewName === 'databases' ? 'block' : 'none';
  if (filesTableView) filesTableView.style.display = viewName === 'files' ? 'block' : 'none';
  if (servicesTableView) servicesTableView.style.display = viewName === 'services' ? 'block' : 'none';
  if (auditTableView) auditTableView.style.display = viewName === 'audit' ? 'block' : 'none';
  if (restoreTableView) restoreTableView.style.display = viewName === 'restore' ? 'block' : 'none';

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
  } else if (viewName === 'restore') {
    makeTableResizable(document.getElementById('restoreTable'));
    sendConsoleAuditEvent('RESTORE', 'VIEW_RESTORE_CONSOLE');
  }

  if (viewName === 'databases' && (!state.items || state.items.length === 0)) {
    loadDatabases();
  } else if (viewName === 'files' && (!state.fileItems || state.fileItems.length === 0)) {
    loadFiles();
  } else if (viewName === 'services' && (!state.servicesList || state.servicesList.length === 0)) {
    loadServices();
  } else if (viewName === 'audit') {
    loadAuditLogs(true);
  } else if (viewName === 'restore' && (!state.restoreItems || state.restoreItems.length === 0)) {
    loadRestoreData();
  }
}

tabBtnDatabases.addEventListener('click', () => switchView('databases'));
tabBtnFiles.addEventListener('click', () => switchView('files'));
if (tabBtnServices) tabBtnServices.addEventListener('click', () => switchView('services'));
if (tabBtnAudit) tabBtnAudit.addEventListener('click', () => switchView('audit'));
if (tabBtnRestore) tabBtnRestore.addEventListener('click', () => switchView('restore'));

function loadCurrentView() {
  if (state.isScanning) return;
  if (state.activeView === 'databases') {
    loadDatabases();
  } else if (state.activeView === 'files') {
    loadFiles();
  } else if (state.activeView === 'services') {
    loadServices();
  } else if (state.activeView === 'audit') {
    loadAuditLogs(true);
  } else if (state.activeView === 'restore') {
    loadRestoreData();
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
  if (state.isScanning) return;
  try {
    const res = await fetch('/api/databases/stats');
    if (!res.ok || state.isScanning) return;
    const stats = await res.json();
    if (state.isScanning) return;

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
    if (!state.isScanning) {
      liveStatusPulse.classList.add('pulse-orange');
    }
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
  if (state.isScanning) return;
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
      databasesTableBody.innerHTML = `<tr class="no-hover"><td colspan="13" style="text-align: center; padding: 0;"><div class="loading-container"><span class="spinner spinner-lg"></span><span>Загрузка информационных баз 1С...</span></div></td></tr>`;
    } else {
      databasesTableBody.style.opacity = '0.5';
    }

    const res = await fetch(`/api/databases?${params.toString()}`);
    if (state.isScanning) return;
    databasesTableBody.style.opacity = '1';

    if (!res.ok) {
      databasesTableBody.innerHTML = `<tr class="no-hover"><td colspan="13" style="text-align: center; color: var(--color-signal-orange); padding: 25px;">Ошибка загрузки данных (${res.status})</td></tr>`;
      return;
    }

    const data = await res.json();
    if (state.isScanning) return;
    state.items = data.items;
    state.total = data.total;
    state.totalPages = data.totalPages;

    renderTable();
    renderPagination();
    updateSortHeaders();
    updateSelectionBar();
  } catch (err) {
    if (state.isScanning) return;
    databasesTableBody.style.opacity = '1';
    console.error('Failed to load databases:', err);
    databasesTableBody.innerHTML = `<tr class="no-hover"><td colspan="13" style="text-align: center; color: var(--color-signal-orange); padding: 25px;">Сетевая ошибка при обращении к веб-сервису</td></tr>`;
  }
}

// Load Files & Sizes (Tab 2)
async function loadFiles(silent = false) {
  if (state.isScanning) return;
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
    if (state.isScanning) return;
    filesTableBody.style.opacity = '1';

    if (!res.ok) {
      filesTableBody.innerHTML = `<tr class="no-hover"><td colspan="10" style="text-align: center; color: var(--color-signal-orange); padding: 25px;">Ошибка загрузки файлов СУБД (${res.status})</td></tr>`;
      return;
    }

    const data = await res.json();
    if (state.isScanning) return;
    state.fileItems = data.items;
    state.filesTotal = data.total;
    state.filesTotalPages = data.totalPages;

    renderFilesTable();
    renderPagination();
    updateFilesSortHeaders();
  } catch (err) {
    if (state.isScanning) return;
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
          <td colspan="13" style="text-align: center; padding: 40px 20px;">
            <div class="loading-container">
              <span class="spinner spinner-lg"></span>
              <div style="font-size: 13px; font-weight: 500; color: var(--color-bone); margin-top: 4px;">Выполняется опрос кластеров 1С...</div>
              <div style="font-size: 11px; color: var(--color-warm-granite);">Идет сбор информационных баз, групп доступа и СУБД. Данные появятся автоматически.</div>
            </div>
          </td>
        </tr>
      `;
    } else {
      databasesTableBody.innerHTML = `<tr class="no-hover"><td colspan="13" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">Базы 1С не найдены в кэше сервиса</td></tr>`;
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
        <td class="${state.sortBy === 'cluster' ? 'col-sorted' : ''}" title="${escapeHtml(b.cluster)}"><code>${escapeHtml(b.cluster)}</code></td>
        <td class="${state.sortBy === 'platform' ? 'col-sorted' : ''}"><span class="badge badge-neutral">${escapeHtml(b.platform || '—')}</span></td>
        <td class="cell-truncate mono ${state.sortBy === 'serviceuser' ? 'col-sorted' : ''}" style="font-size: 11px; color: var(--color-pale-stone);" title="${escapeHtml(b.serviceUser || '—')}">${escapeHtml(b.serviceUser || '—')}</td>
        <td class="cell-truncate ${state.sortBy === 'sql' ? 'col-sorted' : ''}" title="${escapeHtml(sqlServerTitle(b))}"><code>${escapeHtml(b.sqlDisplay || b.sql)}</code></td>
        <td class="cell-truncate ${state.sortBy === 'sqldbname' ? 'col-sorted' : ''}" title="${escapeHtml(b.sqlDbName)}">${escapeHtml(b.sqlDbName)}</td>
        <td>${renderWebPublications(b.webPublications)}</td>
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
  return paths.map(p => `<div class="file-path" title="${escapeHtml(p)}"><code>${escapeHtml(p)}</code></div>`).join('');
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
  if (state.isScanning) {
    paginationInfo.textContent = 'Показано 0-0 из 0 баз';
    currentPageBadge.textContent = '1 / 1';
    btnPrevPage.disabled = true;
    btnNextPage.disabled = true;
    return;
  }
  if (state.activeView === 'restore') {
    const list = filterRestore();
    paginationInfo.textContent = `Показано 1-${list.length} из ${list.length} DEV баз`;
    currentPageBadge.textContent = '1 / 1';
    btnPrevPage.disabled = true;
    btnNextPage.disabled = true;
    return;
  }
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

// DBMS machine name plus the alias the cluster connects through (Consul service or DNS name), if they differ
function sqlServerTitle(b) {
  const host = b.sqlDisplay || b.sql || '';
  return b.sqlHost && b.sql && b.sql.toLowerCase() !== b.sqlHost.toLowerCase()
    ? `${host} (подключение: ${b.sql})`
    : host;
}

// Link to the 1C web publication on IIS: the full address; a narrow column wraps it at "." and "/"
// (never at the hyphen inside a server name)
function renderWebPublications(urls) {
  if (!Array.isArray(urls) || urls.length === 0) {
    return '<span style="color: var(--color-warm-granite);">—</span>';
  }
  // The server keeps one publication per base; data cached by an older version may still hold several
  const url = urls.find(u => /^[a-z]+:\/\/[^/]+\.service\.consul(?::\d+)?\//i.test(u)) || urls[0];
  let label = url;
  try { label = decodeURI(url); } catch { }
  // "http://app-multifront" + ".service" + ".consul" + "/MultiFront": each part stays on one line
  const wrapped = (label.match(/^[a-z][a-z0-9+.-]*:\/\/[^./]*|[./]?[^./]+|[./]/gi) || [label])
    .map(part => `<span class="pub-part">${escapeHtml(part)}</span>`)
    .join('<wbr>');
  return `<div class="pub-item"><a class="pub-link" href="${escapeHtml(url)}" target="_blank" rel="noopener noreferrer" title="${escapeHtml(url)}" onclick="event.stopPropagation();">${wrapped}</a></div>`;
}

// True while the user has selected text on the page: refreshes and row toggles must not destroy it
function hasActiveTextSelection() {
  const selection = window.getSelection();
  return !!selection && !selection.isCollapsed && selection.toString().trim().length > 0;
}

// Row click toggles selection in place (no table re-render), so text can still be selected and copied.
// Clicks on interactive elements and double-clicks (word selection) are ignored.
function toggleRowInPlace(event, selectedSet, key) {
  if (hasActiveTextSelection() || event.detail > 1) return false;
  if (event.target && event.target.closest && event.target.closest('button, a, input, select, .badge-clickable')) return false;

  const selected = !selectedSet.has(key);
  if (selected) selectedSet.add(key); else selectedSet.delete(key);

  const tr = event.currentTarget;
  if (tr && tr.classList) {
    tr.classList.toggle('selected-row', selected);
    const cb = tr.querySelector('.row-checkbox');
    if (cb) cb.checked = selected;
  }
  return true;
}

window.onRowClick = function(event, index) {
  const item = state.items[index];
  if (!item) return;
  if (!toggleRowInPlace(event, state.selectedKeys, getKey(item))) return;

  if (selectAllCheckbox) {
    selectAllCheckbox.checked = state.items.length > 0 && state.items.every(i => state.selectedKeys.has(getKey(i)));
  }
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
  const item = state.fileItems[index];
  if (!item) return;
  if (!toggleRowInPlace(event, state.selectedFilesKeys, getFileKey(item))) return;
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

    const header = "Среда\tБаза 1С\tОписание\tКластер\tПлатформа\tПользователь службы\tСервер СУБД\tБаза в СУБД\tПубликация\tГруппа AD\tIP сервера";
    const rows = selected.map(b => 
      `${b.environment}\t${b.name}\t${b.description || ''}\t${b.cluster}\t${b.platform || ''}\t${b.serviceUser || ''}\t${b.sqlDisplay || b.sql}\t${b.sqlDbName}\t${(b.webPublications || []).join(' ')}\t${b.accessGroup}\t${b.serverIP}`
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
    } else if (state.activeView === 'restore') {
      const items = filterRestore();
      if (items.length === 0) {
        showToast('Список баз для восстановления пуст', 'warning');
        return;
      }
      showToast('Экспорт баз для восстановления в Excel (CSV)...', 'info');
      const headers = ['№', 'Среда', 'Кластер 1С', 'База 1С', 'Сервер СУБД', 'База в СУБД'];
      const rows = items.map((b, i) => [
        i + 1,
        'DEV',
        b.cluster || '',
        b.name || '',
        b.sqlDisplay || b.sql || '',
        b.sqlDbName || ''
      ]);
      downloadCsv(headers, rows, `1C_Restore_DEV_Databases_${formatDate(new Date())}.csv`);
      showToast('Выгрузка баз для восстановления успешно завершена', 'success');
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
    } else if (state.activeView === 'restore') {
      const items = filterRestore();
      if (items.length === 0) {
        showToast('Список баз для восстановления пуст', 'warning');
        return;
      }
      downloadBlob(new Blob([JSON.stringify(items, null, 2)], { type: 'application/json' }), `1C_Restore_DEV_Databases_${formatDate(new Date())}.json`);
      showToast('Выгрузка баз для восстановления в JSON завершена', 'success');
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
const COLUMN_MIN_WIDTH = 36;

function loadColumnWidths(tableId) {
  if (!tableId) return {};
  try { return JSON.parse(localStorage.getItem(`colWidths:${tableId}`) || '{}') || {}; } catch { return {}; }
}

function saveColumnWidth(tableId, index, width) {
  if (!tableId) return;
  try {
    const widths = loadColumnWidths(tableId);
    if (width == null) delete widths[index]; else widths[index] = width;
    localStorage.setItem(`colWidths:${tableId}`, JSON.stringify(widths));
  } catch { }
}

function setColumnWidth(th, width) {
  th.style.width = width + 'px';
  th.style.minWidth = width + 'px';
}

function resetColumnWidth(th) {
  th.style.width = th.dataset.defaultWidth || '';
  th.style.minWidth = th.dataset.defaultMinWidth || '';
}

// Widths dragged on a wider screen must not squeeze the other columns of a fit-to-screen table
// to nothing: they are narrowed in proportion for this window only, the saved widths stay
const FLEX_COLUMN_MIN_WIDTH = 64;

function fitCustomColumnWidths(table) {
  if (!table) return;
  const thList = [...table.querySelectorAll('thead th')];
  const saved = loadColumnWidths(table.id);
  const box = table.closest('.table-responsive');
  if (!table.classList.contains('table-fit') || !box || box.clientWidth === 0) {
    thList.forEach((th, index) => { if (saved[index] && th.querySelector('.col-resizer')) setColumnWidth(th, saved[index]); });
    return;
  }
  const custom = [];
  let reserved = 0;
  thList.forEach((th, index) => {
    if (saved[index] && th.querySelector('.col-resizer')) {
      custom.push([th, saved[index]]);
    } else {
      const width = th.dataset.defaultWidth ?? th.style.width;
      reserved += width.endsWith('px') ? parseFloat(width)
        : width.endsWith('%') ? Math.max(FLEX_COLUMN_MIN_WIDTH, box.clientWidth * parseFloat(width) / 100)
        : FLEX_COLUMN_MIN_WIDTH;
    }
  });
  if (custom.length === 0) return;

  const budget = box.clientWidth - reserved;
  const total = custom.reduce((sum, [, width]) => sum + width, 0);
  const ratio = total > budget ? Math.max(0, budget) / total : 1;
  custom.forEach(([th, width]) => setColumnWidth(th, Math.max(COLUMN_MIN_WIDTH, Math.floor(width * ratio))));
}

let fitColumnsTimer = null;
window.addEventListener('resize', () => {
  clearTimeout(fitColumnsTimer);
  fitColumnsTimer = setTimeout(() => {
    document.querySelectorAll('table.table-fit').forEach(t => { if (t.offsetParent) fitCustomColumnWidths(t); });
  }, 150);
});

// Drag the divider at the right edge of a header to change the column width; the width is remembered
// per table in this browser. Double-click the divider to return the column to its original width.
function makeTableResizable(table) {
  if (!table) return;
  const thList = [...table.querySelectorAll('thead th')];
  thList.forEach((th, index) => {
    if (th.querySelector('.row-checkbox') || th.querySelector('.col-resizer')) return;
    if (index === thList.length - 1) return; // the last column takes the remaining space

    th.dataset.defaultWidth = th.style.width || '';
    th.dataset.defaultMinWidth = th.style.minWidth || '';

    const resizer = document.createElement('div');
    resizer.className = 'col-resizer';
    resizer.title = 'Потяните, чтобы изменить ширину. Двойной щелчок — исходная ширина';
    th.appendChild(resizer);

    let startX = 0;
    let startWidth = 0;
    let guide = null;
    let badge = null;

    const placeOverlay = (clientX, width) => {
      const box = (table.closest('.table-responsive') || table).getBoundingClientRect();
      guide.style.left = clientX + 'px';
      guide.style.top = box.top + 'px';
      guide.style.height = Math.max(0, Math.min(box.bottom, window.innerHeight) - box.top) + 'px';
      badge.textContent = `${width} px`;
      badge.style.left = (clientX + 8) + 'px';
      badge.style.top = (box.top + 6) + 'px';
    };

    // Clicks on the divider must not sort the column
    resizer.addEventListener('click', (e) => e.stopPropagation());

    resizer.addEventListener('dblclick', (e) => {
      e.stopPropagation();
      resetColumnWidth(th);
      saveColumnWidth(table.id, index, null);
    });

    resizer.addEventListener('pointerdown', (e) => {
      if (e.button !== 0) return;
      e.stopPropagation();
      e.preventDefault();
      isColumnResizing = true;
      startX = e.clientX;
      startWidth = th.offsetWidth;
      resizer.setPointerCapture(e.pointerId);
      resizer.classList.add('resizing');
      document.body.style.cursor = 'col-resize';
      document.body.style.userSelect = 'none';

      guide = document.createElement('div');
      guide.className = 'col-resize-guide';
      badge = document.createElement('div');
      badge.className = 'col-resize-badge';
      document.body.append(guide, badge);
      placeOverlay(e.clientX, startWidth);

      const onMove = (ev) => {
        const width = Math.max(COLUMN_MIN_WIDTH, Math.round(startWidth + ev.clientX - startX));
        setColumnWidth(th, width);
        placeOverlay(startX + (width - startWidth), width);
      };

      const onUp = (ev) => {
        resizer.removeEventListener('pointermove', onMove);
        resizer.removeEventListener('pointerup', onUp);
        resizer.removeEventListener('pointercancel', onUp);
        try { resizer.releasePointerCapture(ev.pointerId); } catch { }
        resizer.classList.remove('resizing');
        document.body.style.cursor = '';
        document.body.style.userSelect = '';
        guide?.remove();
        badge?.remove();
        guide = badge = null;
        if (th.offsetWidth !== startWidth) saveColumnWidth(table.id, index, th.offsetWidth);
        // The click that ends the drag must not sort the column
        setTimeout(() => { isColumnResizing = false; }, 60);
      };

      resizer.addEventListener('pointermove', onMove);
      resizer.addEventListener('pointerup', onUp);
      resizer.addEventListener('pointercancel', onUp);
    });
  });
  fitCustomColumnWidths(table);
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
  resetAdAddMemberBar();
  refreshAdManageable(groupName);

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
      // The user may have opened another group while this request was in flight
      if (state.currentAdGroupName !== groupName) return;
      state.currentAdGroupMembers = usersOnly;
      state.currentAdGroupDesc = data.description || 'Группа безопасности Active Directory';
      adModalGroupDesc.textContent = state.currentAdGroupDesc;
      adModalMemberCount.textContent = `${usersOnly.length} участников`;
      adModalLoading.style.display = 'none';
      adModalContent.style.display = 'block';
      renderAdMembers(state.currentAdGroupMembers);
    } else {
      const err = await res.json().catch(() => ({}));
      if (state.currentAdGroupName !== groupName) return;
      adModalLoading.style.display = 'flex';
      adModalLoading.innerHTML = `<span style="color: var(--color-signal-orange); padding: 20px;">${escapeHtml(err.error || 'Не удалось получить состав группы из Active Directory')}</span>`;
      adModalMemberCount.textContent = '0 участников';
    }
  } catch (err) {
    if (state.currentAdGroupName !== groupName) return;
    adModalLoading.style.display = 'flex';
    adModalLoading.innerHTML = `<span style="color: var(--color-signal-orange); padding: 20px;">Ошибка обращения к AD: ${escapeHtml(err.message)}</span>`;
  }
};

function renderAdMembers(members) {
  if (!members || members.length === 0) {
    adMembersTableBody.innerHTML = `<tr><td colspan="8" style="text-align: center; color: #8a8380; padding: 25px 10px;">Участники не найдены</td></tr>`;
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
        ${adGroupManageable ? `<td style="padding: 2px 4px; text-align: center;"><button class="btn btn-ghost btn-sm btn-ad-remove" data-sam="${escapeHtml(m.samAccountName || '')}" title="Удалить ${escapeHtml(name)} из группы">✕</button></td>` : ''}
      </tr>
    `;
  });

  adMembersTableBody.innerHTML = html;
}

// AD group membership management (add / remove members)
let adGroupManageable = false;
let adSelectedUser = null;
let adSuggestTimer = null;
let adSuggestSeq = 0;
let adSuggestItems = [];

function rerenderAdMembers() {
  adMemberSearchInput.dispatchEvent(new Event('input'));
}

function resetAdAddMemberBar() {
  adGroupManageable = false;
  adSelectedUser = null;
  adSuggestItems = [];
  if (adAddMemberBar) adAddMemberBar.style.display = 'none';
  if (adMembersActionsHeader) adMembersActionsHeader.style.display = 'none';
  if (adAddMemberInput) adAddMemberInput.value = '';
  if (adAddMemberSuggest) adAddMemberSuggest.style.display = 'none';
  if (btnAdAddMember) btnAdAddMember.disabled = true;
}

async function refreshAdManageable(groupName) {
  try {
    const res = await fetch(`/api/activedirectory/group/${encodeURIComponent(groupName)}/manageable`);
    if (!res.ok) return;
    const data = await res.json();
    if (state.currentAdGroupName !== groupName) return;
    adGroupManageable = data.manageable === true;
    if (adAddMemberBar) adAddMemberBar.style.display = adGroupManageable ? 'block' : 'none';
    if (adMembersActionsHeader) adMembersActionsHeader.style.display = adGroupManageable ? '' : 'none';
    if (adModalContent.style.display !== 'none') rerenderAdMembers();
  } catch {
    // management controls stay hidden if the check fails
  }
}

function syncAdGroupState() {
  const g = state.currentAdGroupName;
  window.__adGroupCache[g] = {
    groupName: g,
    description: state.currentAdGroupDesc,
    members: state.currentAdGroupMembers
  };
  adModalMemberCount.textContent = `${state.currentAdGroupMembers.length} участников`;
  adModalLoading.style.display = 'none';
  adModalContent.style.display = 'block';
  rerenderAdMembers();
}

function isCurrentAdMember(sam) {
  const s = (sam || '').toLowerCase();
  return state.currentAdGroupMembers.some(m => (m.samAccountName || '').toLowerCase() === s);
}

function renderAdSuggest(items) {
  adSuggestItems = items;
  if (!items.length) {
    adAddMemberSuggest.innerHTML = '<div class="ad-suggest-empty">Пользователи не найдены</div>';
    adAddMemberSuggest.style.display = 'block';
    return;
  }
  adAddMemberSuggest.innerHTML = items.map((u, idx) => {
    const member = isCurrentAdMember(u.samAccountName);
    const meta = [u.title, u.department].filter(Boolean).join(' · ');
    const flags = member
      ? '<span class="badge badge-neutral">В группе</span>'
      : (u.enabled === false ? '<span class="badge badge-missing">Отключен</span>' : '');
    return `
      <div class="ad-suggest-item ${member ? 'is-member' : ''}" data-idx="${idx}">
        <span class="ad-suggest-name">${escapeHtml(u.displayName || u.samAccountName)}</span>
        <span class="ad-suggest-sam">${escapeHtml(u.samAccountName)}</span>
        <span class="ad-suggest-meta" title="${escapeHtml(meta)}">${escapeHtml(meta)}</span>
        ${flags}
      </div>
    `;
  }).join('');
  adAddMemberSuggest.style.display = 'block';
}

function selectAdSuggest(idx) {
  const u = adSuggestItems[idx];
  if (!u || isCurrentAdMember(u.samAccountName)) return;
  adSelectedUser = u;
  adAddMemberInput.value = `${u.displayName || u.samAccountName} (${u.samAccountName})`;
  adAddMemberSuggest.style.display = 'none';
  btnAdAddMember.disabled = false;
  btnAdAddMember.focus();
}

async function addAdMember() {
  const groupName = state.currentAdGroupName;
  const typed = adAddMemberInput.value.trim();
  const sam = adSelectedUser ? adSelectedUser.samAccountName : typed;
  if (!groupName || !sam) return;
  if (!adSelectedUser && /\s/.test(sam)) {
    showToast('Выберите пользователя из списка или введите точный логин', 'warning');
    return;
  }

  btnAdAddMember.disabled = true;
  btnAdAddMember.innerHTML = '<span class="spinner spinner-sm"></span> Добавление...';
  try {
    const res = await fetch(`/api/activedirectory/group/${encodeURIComponent(groupName)}/members`, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ samAccountName: sam })
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || data.success !== true) throw new Error(data.message || `HTTP ${res.status}`);

    if (state.currentAdGroupName === groupName && !isCurrentAdMember(sam)) {
      const added = adSelectedUser || { samAccountName: sam, displayName: sam, enabled: true };
      state.currentAdGroupMembers = [...state.currentAdGroupMembers, { ...added, isGroup: false }]
        .sort((a, b) => (a.displayName || '').localeCompare(b.displayName || '', 'ru'));
      syncAdGroupState();
    }
    adSelectedUser = null;
    adAddMemberInput.value = '';
    showToast(data.message || `${sam} добавлен в группу ${groupName}`, 'success');
  } catch (err) {
    showToast(`Не удалось добавить ${sam}: ${err.message}`, 'error');
  } finally {
    btnAdAddMember.textContent = 'Добавить в группу';
    btnAdAddMember.disabled = !adAddMemberInput.value.trim();
  }
}

async function removeAdMember(sam, btn) {
  const groupName = state.currentAdGroupName;
  if (!groupName || !sam) return;
  const member = state.currentAdGroupMembers.find(m => (m.samAccountName || '').toLowerCase() === sam.toLowerCase());
  const label = member && member.displayName ? `${member.displayName} (${sam})` : sam;
  if (!confirm(`Удалить ${label} из группы ${groupName}?`)) return;

  if (btn) btn.disabled = true;
  try {
    const res = await fetch(`/api/activedirectory/group/${encodeURIComponent(groupName)}/members/${encodeURIComponent(sam)}`, {
      method: 'DELETE'
    });
    const data = await res.json().catch(() => ({}));
    if (!res.ok || data.success !== true) throw new Error(data.message || `HTTP ${res.status}`);

    if (state.currentAdGroupName === groupName) {
      state.currentAdGroupMembers = state.currentAdGroupMembers
        .filter(m => (m.samAccountName || '').toLowerCase() !== sam.toLowerCase());
      syncAdGroupState();
    }
    showToast(data.message || `${sam} удалён из группы ${groupName}`, 'success');
  } catch (err) {
    if (btn) btn.disabled = false;
    showToast(`Не удалось удалить ${sam}: ${err.message}`, 'error');
  }
}

if (adAddMemberInput) {
  adAddMemberInput.addEventListener('input', () => {
    adSelectedUser = null;
    const q = adAddMemberInput.value.trim();
    btnAdAddMember.disabled = q.length < 2;
    clearTimeout(adSuggestTimer);
    if (q.length < 2) {
      adAddMemberSuggest.style.display = 'none';
      return;
    }
    adSuggestTimer = setTimeout(async () => {
      const seq = ++adSuggestSeq;
      try {
        const res = await fetch(`/api/activedirectory/users/search?q=${encodeURIComponent(q)}`);
        const data = await res.json().catch(() => []);
        if (seq !== adSuggestSeq) return;
        if (!res.ok) throw new Error(data.error || `HTTP ${res.status}`);
        renderAdSuggest(Array.isArray(data) ? data : []);
      } catch (err) {
        if (seq !== adSuggestSeq) return;
        adAddMemberSuggest.innerHTML = `<div class="ad-suggest-empty">${escapeHtml(err.message)}</div>`;
        adAddMemberSuggest.style.display = 'block';
      }
    }, 300);
  });

  adAddMemberInput.addEventListener('keydown', (e) => {
    if (e.key === 'Enter') {
      e.preventDefault();
      if (!btnAdAddMember.disabled) addAdMember();
    } else if (e.key === 'Escape') {
      adAddMemberSuggest.style.display = 'none';
    }
  });
}

if (adAddMemberSuggest) {
  adAddMemberSuggest.addEventListener('click', (e) => {
    const item = e.target.closest('.ad-suggest-item');
    if (item) selectAdSuggest(parseInt(item.dataset.idx, 10));
  });
}

if (btnAdAddMember) btnAdAddMember.addEventListener('click', addAdMember);

adMembersTableBody.addEventListener('click', (e) => {
  const btn = e.target.closest('.btn-ad-remove');
  if (btn) removeAdMember(btn.dataset.sam, btn);
});

document.addEventListener('click', (e) => {
  if (adAddMemberBar && adAddMemberSuggest && !adAddMemberBar.contains(e.target)) {
    adAddMemberSuggest.style.display = 'none';
  }
});

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
  modalSubtitle.textContent = `Кластер: ${item.cluster} | СУБД: ${sqlServerTitle(item)} [${item.sqlDbName}]`;

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

function resetUiToScanningState() {
  state.isScanning = true;
  if (btnScan) {
    btnScan.disabled = true;
    btnScan.innerHTML = `<span class="spinner" style="border-top-color: var(--color-metric-green); width: 11px; height: 11px; margin-right: 4px;"></span> Сканирование...`;
  }
  if (liveStatusPulse) {
    liveStatusPulse.classList.add('pulse-orange');
  }

  // Сброс сводных плашек метрик в исходное состояние загрузки
  const metricTotal = document.getElementById('metricTotal');
  if (metricTotal) metricTotal.textContent = '0';

  const metricDevProd = document.getElementById('metricDevProd');
  if (metricDevProd) metricDevProd.textContent = 'PROD: 0 | DEV: 0';

  const metricClusters = document.getElementById('metricClusters');
  if (metricClusters) metricClusters.textContent = '0';

  const btnCount = document.getElementById('btnClusterHealthCount');
  if (btnCount) btnCount.textContent = '';

  const clustersSub = document.getElementById('metricClustersSub');
  if (clustersSub) clustersSub.textContent = 'Опрос кластеров...';

  const metricSqlServers = document.getElementById('metricSqlServers');
  if (metricSqlServers) metricSqlServers.textContent = '0';

  const sqlSub = document.getElementById('metricSqlSub');
  if (sqlSub) sqlSub.textContent = 'Сбор данных...';

  const metricAdCoverage = document.getElementById('metricAdCoverage');
  if (metricAdCoverage) metricAdCoverage.textContent = '0%';

  const metricAdCount = document.getElementById('metricAdCount');
  if (metricAdCount) metricAdCount.textContent = '0 с группами AD';

  const metricLastScan = document.getElementById('metricLastScan');
  if (metricLastScan) metricLastScan.textContent = 'Опрос: сканирование...';

  // Очистка наборов данных
  state.items = [];
  state.total = 0;
  state.totalPages = 0;
  state.page = 1;

  state.fileItems = [];
  state.filesTotal = 0;
  state.filesTotalPages = 0;
  state.filesPage = 1;

  if (state.selectedKeys) state.selectedKeys.clear();
  if (state.selectedFilesKeys) state.selectedFilesKeys.clear();
  if (selectAllCheckbox) selectAllCheckbox.checked = false;
  if (selectFilesAllCheckbox) selectFilesAllCheckbox.checked = false;
  updateSelectionBar();

  // Очистка таблиц и вывод анимации загрузки (спиннер первого сканирования)
  if (databasesTableBody) {
    databasesTableBody.style.opacity = '1';
    databasesTableBody.innerHTML = `
      <tr class="no-hover">
        <td colspan="13" style="text-align: center; padding: 0;">
          <div class="loading-container">
            <span class="spinner spinner-lg"></span>
            <span>Загрузка информационных баз 1С...</span>
          </div>
        </td>
      </tr>
    `;
  }
  if (filesTableBody) {
    filesTableBody.style.opacity = '1';
    filesTableBody.innerHTML = `
      <tr class="no-hover">
        <td colspan="10" style="text-align: center; padding: 0;">
          <div class="loading-container">
            <span class="spinner spinner-lg"></span>
            <span>Сбор сведений о размерах и файлах баз данных СУБД...</span>
          </div>
        </td>
      </tr>
    `;
  }

  // Сброс пагинации
  if (paginationInfo) paginationInfo.textContent = 'Показано 0-0 из 0 баз';
  if (currentPageBadge) currentPageBadge.textContent = '1 / 1';
  if (btnPrevPage) btnPrevPage.disabled = true;
  if (btnNextPage) btnNextPage.disabled = true;
}

async function executeDatabaseScan() {
  if (state.isScanning) return;

  try {
    resetUiToScanningState();
    const res = await fetch('/api/sync/scan', { method: 'POST' });
    if (res.ok || res.status === 202) {
      showToast('Сканирование кластеров запущено.', 'info');
      
      let checkCount = 0;
      const pollInterval = setInterval(async () => {
        checkCount++;
        try {
          const statusRes = await fetch('/api/sync/status');
          if (statusRes.ok) {
            const status = await statusRes.json();
            const isScanning = status.isScanning ?? status.IsScanning;
            if (!isScanning || checkCount > 90) {
              clearInterval(pollInterval);
              state.isScanning = false;
              if (btnScan) {
                btnScan.disabled = false;
                btnScan.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="scan-icon"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Обновить базы`;
              }
              if (liveStatusPulse) {
                liveStatusPulse.classList.remove('pulse-orange');
              }
              await loadStats();
              await loadFilters();
              if (state.activeView === 'files') {
                await loadFiles();
                await loadDatabases();
              } else {
                await loadDatabases();
                await loadFiles(true);
              }
              showToast('Сканирование завершено! Таблица обновлена.', 'success');
            }
          }
        } catch {
          // ignore transient poll errors
        }
      }, 1000);
    } else {
      const err = await res.json().catch(() => ({}));
      showToast(err.message || 'Ошибка запуска сканирования', 'error');
      state.isScanning = false;
      if (btnScan) {
        btnScan.disabled = false;
        btnScan.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="scan-icon"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Обновить базы`;
      }
      if (liveStatusPulse) {
        liveStatusPulse.classList.remove('pulse-orange');
      }
      await loadStats();
      loadCurrentView();
    }
  } catch (err) {
    showToast(`Ошибка: ${err.message}`, 'error');
    state.isScanning = false;
    if (btnScan) {
      btnScan.disabled = false;
      btnScan.innerHTML = `<svg width="11" height="11" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" class="scan-icon"><path d="M21.5 2v6h-6M21.34 15.57a10 10 0 1 1-.57-8.38l5.67-5.67"/></svg> Обновить базы`;
    }
    if (liveStatusPulse) {
      liveStatusPulse.classList.remove('pulse-orange');
    }
    await loadStats();
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
  // Quotes are escaped too: the result is also inserted into title="..." / data-*="..." attributes
  return String(text)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
    .replace(/'/g, '&#39;');
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
    // Do not re-render tables while the user is selecting or copying text
    if (hasActiveTextSelection()) return;
    if (!state.isScanning) {
      if (state.activeView === 'audit') {
        if (!document.activeElement || document.activeElement !== auditSearchInput) {
          loadAuditLogs(true);
        }
      } else {
        loadStats(true);
      }
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
state.restoreItems = [];
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
let restoreUnlocked = false;
try {
  sessionStorage.removeItem('sec_srv_unlocked');
  sessionStorage.removeItem('sec_audit_unlocked');
  sessionStorage.removeItem('sec_restore_unlocked');
} catch {}
if (tabBtnServices) tabBtnServices.style.display = 'none';
if (tabBtnAudit) tabBtnAudit.style.display = 'none';
if (tabBtnRestore) tabBtnRestore.style.display = 'none';

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

function toggleRestoreDatabases() {
  restoreUnlocked = !restoreUnlocked;
  if (!restoreUnlocked) {
    if (tabBtnRestore) tabBtnRestore.style.display = 'none';
    if (state.activeView === 'restore') {
      switchView('databases');
    }
    showToast('Консоль восстановления баз скрыта', 'info');
    sendConsoleAuditEvent('RESTORE', 'CLOSE_RESTORE_CONSOLE');
  } else {
    if (tabBtnRestore) tabBtnRestore.style.display = 'inline-flex';
    showToast('Инженерная консоль: Восстановление баз открыта', 'success');
    sendConsoleAuditEvent('RESTORE', 'OPEN_RESTORE_CONSOLE');
  }
}

window.toggleServicesManagement = toggleServicesManagement;
window.toggleAuditLog = toggleAuditLog;
window.toggleRestoreDatabases = toggleRestoreDatabases;

// 1. Mouse Click with Ctrl + Alt (captured at root level for 100% reliability)
document.addEventListener('click', (e) => {
  const isModifierCombo = (e.ctrlKey && e.altKey) || (e.metaKey && e.altKey);
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

  // Check Restore Bases click on paginationInfo (bottom-left corner)
  const paginationTarget = e.target.closest('#paginationInfo') || e.target.closest('.pagination-info');
  if (paginationTarget) {
    e.preventDefault();
    e.stopPropagation();
    toggleRestoreDatabases();
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

const SERVICE_ACTIONS = {
  'start': {
    title: 'Запуск службы 1С',
    confirm: 'Запустить',
    busy: 'Запуск…',
    hint: 'Запустить службу 1С и RAS',
    danger: false,
    icon: '<svg width="12" height="12" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><path d="M7 4.5v15l12-7.5z"/></svg>'
  },
  'stop': {
    title: 'Остановка службы 1С',
    confirm: 'Остановить',
    busy: 'Остановка…',
    hint: 'Остановить службу 1С и RAS',
    danger: true,
    icon: '<svg width="11" height="11" viewBox="0 0 24 24" fill="currentColor" aria-hidden="true"><rect x="5" y="5" width="14" height="14" rx="1.5"/></svg>'
  },
  'restart': {
    title: 'Перезапуск службы 1С',
    confirm: 'Перезапустить',
    busy: 'Перезапуск…',
    hint: 'Перезапустить службу 1С и RAS',
    danger: true,
    icon: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M21 12a9 9 0 1 1-2.64-6.36"/><path d="M21 3v6h-6"/></svg>'
  },
  'restart-clean-cache': {
    title: 'Перезапуск с очисткой кэша',
    confirm: 'Перезапустить и очистить',
    busy: 'Очистка кэша…',
    hint: 'Перезапустить с очисткой серверного кэша snccntx*',
    danger: true,
    icon: '<svg width="13" height="13" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="m7 21-4.3-4.3c-1-1-1-2.5 0-3.4l9.6-9.6c1-1 2.5-1 3.4 0l5.6 5.6c1 1 1 2.5 0 3.4L13 21"/><path d="M22 21H7"/><path d="m5 11 9 9"/></svg>'
  }
};

const SERVICE_STATES = {
  'running': { text: 'Работает', cls: 'status-text-running' },
  'stopped': { text: 'Остановлена', cls: 'status-text-stopped' },
  'start pending': { text: 'Запускается', cls: 'status-text-pending' },
  'stop pending': { text: 'Останавливается', cls: 'status-text-pending' }
};

state.servicesBusy = new Map();

function serviceKey(s) {
  return `${(s.host || '').toLowerCase()}|${(s.serviceName || '').toLowerCase()}`;
}

function serviceStateView(status) {
  return SERVICE_STATES[(status || '').toLowerCase()] || { text: status || 'Неизвестно', cls: 'status-text-pending' };
}

// "Агент сервера 1С:Предприятия 8.3 (x86-64) Бухгалтерия" -> "Бухгалтерия": the common prefix repeats on every row
function serviceShortName(s) {
  const full = s.displayName || s.serviceName || '';
  const rest = full.replace(/^Агент сервера 1С:Предприятия\s*8\.\d+\s*(\(x86(-64)?\))?\s*/i, '').trim();
  return rest || full;
}

function serviceActionButtons(s, idx) {
  const st = (s.status || '').toLowerCase();
  const allowed = {
    'start': st !== 'running',
    'stop': st !== 'stopped',
    'restart': st === 'running',
    'restart-clean-cache': true
  };
  return Object.entries(SERVICE_ACTIONS).map(([action, meta]) => `
    <button type="button" class="svc-btn${meta.danger ? ' svc-btn-danger' : ''}" ${allowed[action] ? '' : 'disabled'}
      data-action="${action}" data-idx="${idx}" title="${meta.hint}" aria-label="${meta.hint}">${meta.icon}</button>`).join('');
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
    const busyAction = state.servicesBusy.get(serviceKey(s));
    const stateView = serviceStateView(s.status);
    const statusBadge = busyAction
      ? `<span class="status-text-pending">—</span>`
      : `<span class="${stateView.cls}">${escapeHtml(stateView.text)}</span>`;

    const envBadge = s.environment === 'PROD'
      ? `<span class="badge badge-prod">PROD</span>`
      : `<span class="badge badge-dev">DEV</span>`;

    let rasInfo = `<span class="mono" style="color: var(--color-warm-granite);" title="Служба RAS не найдена">—</span>`;
    if (s.rasPort && s.rasPort > 0) {
      const rasState = serviceStateView(s.rasStatus);
      const rasDot = (s.rasStatus || '').toLowerCase() === 'running' ? 'ras-dot-on' : 'ras-dot-off';
      rasInfo = `<span class="ras-port" title="${escapeHtml(`RAS ${s.rasServiceName || ''}: ${rasState.text}`)}"><span class="ras-dot ${rasDot}"></span>${s.rasPort}</span>`;
    }

    const version = s.platformVersion && s.platformVersion !== 'Unknown' ? s.platformVersion : '';
    const actions = busyAction
      ? `<span class="svc-busy"><span class="spinner"></span>${escapeHtml(SERVICE_ACTIONS[busyAction]?.busy || '')}</span>`
      : `<div class="svc-actions" role="group" aria-label="Управление службой">${serviceActionButtons(s, idx)}</div>`;

    return `
      <tr>
        <td style="text-align: center; font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-warm-granite);">${idx + 1}</td>
        <td style="text-align: center;">${envBadge}</td>
        <td class="mono" style="font-weight: 600; color: var(--color-bone);">${escapeHtml(s.host)}</td>
        <td style="text-align: center;" class="mono">${s.clusterPort}</td>
        <td title="${escapeHtml(`${s.displayName || ''}\n${s.serviceName || ''}`)}">
          <div class="svc-name">${escapeHtml(serviceShortName(s))}</div>
          ${version ? `<div class="svc-meta">${escapeHtml(version)}</div>` : ''}
        </td>
        <td style="text-align: center;">${statusBadge}</td>
        <td class="mono" style="font-size: 11px; color: var(--color-pale-stone);">${escapeHtml(s.startName || '—')}</td>
        <td class="mono" style="font-size: 10px; color: var(--color-warm-granite);" title="${escapeHtml(s.clusterDir)}">${escapeHtml(s.clusterDir || '—')}</td>
        <td style="text-align: center;">${rasInfo}</td>
        <td style="text-align: center;">${actions}</td>
      </tr>
    `;
  }).join('');

  updateServicesSortHeaders();

  servicesTableBody.querySelectorAll('.svc-btn').forEach(btn => {
    btn.addEventListener('click', () => {
      const service = list[parseInt(btn.dataset.idx, 10)];
      if (!service || state.servicesBusy.has(serviceKey(service))) return;
      openServiceConfirmModal(service, btn.dataset.action);
    });
  });
}

function closeServiceConfirmModal() {
  if (serviceConfirmModal) {
    serviceConfirmModal.classList.remove('open');
    serviceConfirmModal.style.display = 'none';
  }
  state.pendingServiceAction = null;
}

function openServiceConfirmModal(service, action) {
  const meta = SERVICE_ACTIONS[action];
  if (!meta) return;
  state.pendingServiceAction = { service, action };

  if (confirmModalTitle) confirmModalTitle.textContent = meta.title;
  if (confirmModalBodyText) {
    confirmModalBodyText.innerHTML = `
      <div class="svc-confirm-grid">
        <span>Служба</span><strong>${escapeHtml(service.displayName || service.serviceName)}</strong>
        <span>Сервер</span><span class="mono">${escapeHtml(service.host)}</span>
        <span>Порт кластера</span><span class="mono">${escapeHtml(String(service.clusterPort))}</span>
        ${service.rasServiceName ? `<span>RAS</span><span class="mono">${escapeHtml(service.rasServiceName)}${service.rasPort ? ` · ${service.rasPort}` : ''}</span>` : ''}
        ${action === 'restart-clean-cache' ? `<span>Каталог кластера</span><span class="mono">${escapeHtml(service.clusterDir || 'не определён')}</span>` : ''}
      </div>
    `;
  }

  if (confirmModalWarning) {
    const port = escapeHtml(String(service.clusterPort));
    const warnings = {
      'stop': `Кластер на порту ${port} станет недоступен: сеансы пользователей будут завершены, рабочие процессы rphost и rmngr остановлены.`,
      'restart': `Все сеансы пользователей кластера на порту ${port} будут завершены.`,
      'restart-clean-cache': service.clusterDir
        ? `Служба будет остановлена, из каталога кластера удалены папки сеансового кэша <code>snccntx*</code>, затем служба запустится. Все сеансы пользователей будут завершены.`
        : `Каталог кластера не определён: служба будет перезапущена без очистки кэша, операция завершится с ошибкой.`
    };
    confirmModalWarning.innerHTML = warnings[action] || '';
    confirmModalWarning.style.display = warnings[action] ? 'block' : 'none';
  }

  if (confirmModalSpinner) confirmModalSpinner.style.display = 'none';
  if (confirmModalButtons) confirmModalButtons.style.display = 'flex';
  if (btnExecuteServiceAction) {
    btnExecuteServiceAction.disabled = false;
    btnExecuteServiceAction.textContent = meta.confirm;
  }
  if (serviceConfirmModal) {
    serviceConfirmModal.classList.add('open');
    serviceConfirmModal.style.display = 'flex';
  }
}

if (confirmModalClose) confirmModalClose.addEventListener('click', closeServiceConfirmModal);
if (btnCancelServiceAction) btnCancelServiceAction.addEventListener('click', closeServiceConfirmModal);
if (serviceConfirmModal) {
  serviceConfirmModal.addEventListener('click', (e) => {
    if (e.target === serviceConfirmModal) closeServiceConfirmModal();
  });
}

// The modal closes right away; progress is shown in the row, so other services stay manageable
async function executeServiceAction(service, action) {
  const key = serviceKey(service);
  state.servicesBusy.set(key, action);
  renderServicesTable();

  try {
    const res = await fetch('/api/services/action', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ host: service.host, serviceName: service.serviceName, action })
    });
    let result;
    try {
      result = await res.json();
    } catch {
      throw new Error(`HTTP ${res.status}`);
    }

    // The list may have been reloaded meanwhile: patch the current object, not the captured one
    const current = (state.servicesList || []).find(s => serviceKey(s) === key);
    if (current) {
      if (result.currentStatus && result.currentStatus !== 'Unknown') current.status = result.currentStatus;
      if (result.rasStatus) current.rasStatus = result.rasStatus;
    }
    showToast(result.message || (result.success ? 'Операция выполнена' : `Ошибка выполнения операции (HTTP ${res.status})`),
      result.success ? 'success' : 'error');
  } catch (err) {
    showToast('Ошибка при вызове операции: ' + err.message, 'error');
  } finally {
    state.servicesBusy.delete(key);
    renderServicesTable();
  }
}

if (btnExecuteServiceAction) {
  btnExecuteServiceAction.addEventListener('click', () => {
    if (!state.pendingServiceAction) return;
    const { service, action } = state.pendingServiceAction;
    closeServiceConfirmModal();
    executeServiceAction(service, action);
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
  const hasExisting = state.auditList && state.auditList.length > 0;
  if (!hasExisting) {
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
  } else {
    auditTableBody.style.opacity = '0.6';
  }
  try {
    const search = auditSearchInput ? auditSearchInput.value.trim() : '';
    const res = await fetch(`/api/services/audit?limit=300&search=${encodeURIComponent(search)}`);
    auditTableBody.style.opacity = '1';
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const contentType = res.headers.get('content-type') || '';
    if (!contentType.includes('application/json')) {
      throw new Error('Бэкенд вернул HTML вместо JSON. Убедитесь, что служба запущена со свежей сборкой.');
    }
    state.auditList = await res.json() || [];
    renderAuditTable();
  } catch (err) {
    auditTableBody.style.opacity = '1';
    if (hasExisting) {
      if (typeof showToast === 'function') {
        showToast(`Не удалось обновить журнал аудита: ${err.message}`, 'warning');
      }
      renderAuditTable();
    } else {
      auditTableBody.innerHTML = `
        <tr>
          <td colspan="9" style="text-align: center; padding: 30px; color: var(--color-signal-orange);">
            <div style="font-weight: 500; margin-bottom: 8px;">Ошибка загрузки журнала аудита: ${escapeHtml(err.message)}</div>
            <div style="font-size: 11.5px; color: var(--color-warm-granite); margin-bottom: 14px;">Возможно, служба перезапускается или временно недоступна по сети.</div>
            <button class="btn btn-secondary btn-sm" onclick="window.loadAuditLogs ? window.loadAuditLogs(true) : loadAuditLogs(true)" style="cursor: pointer;">
              Повторить попытку
            </button>
          </td>
        </tr>
      `;
    }
  }
}
window.loadAuditLogs = loadAuditLogs;

function parseAuditTimestamp(item) {
  if (!item) return 0;
  if (item.timestampUtc) {
    const t = new Date(item.timestampUtc).getTime();
    if (!isNaN(t) && t > 0) return t;
  }
  if (item.timestamp) {
    if (typeof item.timestamp === 'number') return item.timestamp;
    const t = new Date(item.timestamp).getTime();
    if (!isNaN(t) && t > 0) return t;
  }
  if (item.timestampLocal) {
    const p = item.timestampLocal.split(/[\s.:]+/);
    if (p.length >= 6) {
      const t = new Date(Number(p[2]), Number(p[1]) - 1, Number(p[0]), Number(p[3]), Number(p[4]), Number(p[5])).getTime();
      if (!isNaN(t) && t > 0) return t;
    }
    const t = new Date(item.timestampLocal).getTime();
    if (!isNaN(t) && t > 0) return t;
  }
  return 0;
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
    if (sortBy === 'timestamp' || sortBy === 'timestampUtc' || sortBy === 'timestampLocal') {
      const tA = parseAuditTimestamp(a);
      const tB = parseAuditTimestamp(b);
      return sortDir === 'asc' ? tA - tB : tB - tA;
    }
    if (sortBy === 'durationMs' || sortBy === 'clusterPort' || sortBy === 'rasPort') {
      const valA = Number(a[sortBy]) || 0;
      const valB = Number(b[sortBy]) || 0;
      return sortDir === 'asc' ? valA - valB : valB - valA;
    }
    let valA = (a[sortBy] ?? '').toString().toLowerCase();
    let valB = (b[sortBy] ?? '').toString().toLowerCase();
    return sortDir === 'asc' ? valA.localeCompare(valB, 'ru', { numeric: true }) : valB.localeCompare(valA, 'ru', { numeric: true });
  });
}

const AUDIT_CLIENT_PALETTE = [
  { color: '#5dade2', glow: 'rgba(93, 173, 226, 0.45)', bg: 'rgba(93, 173, 226, 0.10)', border: 'rgba(93, 173, 226, 0.28)' }, // Slate Sky
  { color: '#58d68d', glow: 'rgba(88, 214, 141, 0.45)', bg: 'rgba(88, 214, 141, 0.10)', border: 'rgba(88, 214, 141, 0.28)' }, // Sage Mint
  { color: '#f5b041', glow: 'rgba(245, 176, 65, 0.45)', bg: 'rgba(245, 176, 65, 0.10)', border: 'rgba(245, 176, 65, 0.28)' }, // Warm Amber
  { color: '#af7ac5', glow: 'rgba(175, 122, 197, 0.45)', bg: 'rgba(175, 122, 197, 0.10)', border: 'rgba(175, 122, 197, 0.28)' }, // Amethyst Slate
  { color: '#48c9b0', glow: 'rgba(72, 201, 176, 0.45)', bg: 'rgba(72, 201, 176, 0.10)', border: 'rgba(72, 201, 176, 0.28)' }, // Pale Teal
  { color: '#f1948a', glow: 'rgba(241, 148, 138, 0.45)', bg: 'rgba(241, 148, 138, 0.10)', border: 'rgba(241, 148, 138, 0.28)' }, // Dusty Rose
  { color: '#7fb3d5', glow: 'rgba(127, 179, 213, 0.45)', bg: 'rgba(127, 179, 213, 0.10)', border: 'rgba(127, 179, 213, 0.28)' }, // Steel Blue
  { color: '#eb984e', glow: 'rgba(235, 152, 78, 0.45)', bg: 'rgba(235, 152, 78, 0.10)', border: 'rgba(235, 152, 78, 0.28)' }, // Copper Ochre
  { color: '#d7bde2', glow: 'rgba(215, 189, 226, 0.45)', bg: 'rgba(215, 189, 226, 0.10)', border: 'rgba(215, 189, 226, 0.28)' }, // Lavender Gray
  { color: '#a2d9ce', glow: 'rgba(162, 217, 206, 0.45)', bg: 'rgba(162, 217, 206, 0.10)', border: 'rgba(162, 217, 206, 0.28)' }  // Seafoam Slate
];

function getAuditClientPalette(key) {
  if (!key || key === '-' || key === '—') {
    return { color: 'var(--color-warm-granite)', glow: 'transparent', bg: 'transparent', border: 'transparent' };
  }
  let hash = 0;
  for (let i = 0; i < key.length; i++) {
    hash = ((hash << 5) - hash) + key.charCodeAt(i);
    hash |= 0;
  }
  const idx = Math.abs(hash) % AUDIT_CLIENT_PALETTE.length;
  return AUDIT_CLIENT_PALETTE[idx];
}

function toggleAuditClientFilter(key) {
  if (!auditSearchInput) return;
  const currentVal = auditSearchInput.value.trim().toLowerCase();
  const targetVal = key.trim().toLowerCase();
  if (currentVal === targetVal) {
    auditSearchInput.value = '';
    showToast('Фильтр по инженеру сброшен', 'info');
  } else {
    auditSearchInput.value = key;
    showToast(`Фильтр по инженеру: ${key} (кликните повторно для сброса)`, 'info');
  }
  renderAuditTable();
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
    const statusCode = (e.status || '').toUpperCase();
    const isSuccess = statusCode === 'SUCCESS';
    // QUEUED marks the start of a long operation (restore); its outcome is a separate entry
    const isInfo = statusCode === 'QUEUED';
    const statusBadge = isSuccess
      ? `<span class="audit-status-success">Успех</span>`
      : (isInfo
        ? `<span class="audit-status-info">Запущено</span>`
        : `<span class="audit-status-failed">Ошибка</span>`);
    const resultText = e.errorMessage || (isSuccess ? 'Операция выполнена успешно' : '');
    const resultColor = isSuccess || isInfo ? 'var(--color-pale-stone)' : 'var(--color-signal-orange)';

    const actionBadge = `<span class="mono" style="font-size: 11px; color: var(--color-bone);">${escapeHtml(e.action)}</span>`;

    const serverPortDisplay = (e.clusterPort && e.clusterPort > 0)
      ? `${escapeHtml(e.host)}:${e.clusterPort}`
      : ((e.rasPort && e.rasPort > 0)
        ? `${escapeHtml(e.host)}:${e.rasPort}`
        : `<span style="color: var(--color-warm-granite);">—</span>`);

    const durationSec = ((Number(e.durationMs) || 0) / 1000).toFixed(2) + ' с';
    let cleanIp = (e.clientIp || '').trim();
    if (cleanIp.startsWith('::ffff:')) cleanIp = cleanIp.substring(7);
    if (cleanIp === '::1') cleanIp = '127.0.0.1';
    const clientKey = cleanIp || (e.clientHostName || '').trim() || '—';
    const pal = getAuditClientPalette(clientKey);
    const clientTitle = e.clientHostName
      ? `${escapeHtml(e.clientHostName)} (${escapeHtml(cleanIp)})`
      : escapeHtml(cleanIp);

    const clientPill = clientKey !== '—'
      ? `<div class="audit-client-pill" data-client="${escapeHtml(clientKey)}" style="--client-color: ${pal.color}; --client-bg: ${pal.bg}; --client-border: ${pal.border}; --client-glow: ${pal.glow};" title="${clientTitle} • Нажмите для быстрой фильтрации">
           <span class="audit-client-dot"></span>
           <span class="audit-client-ip">${escapeHtml(cleanIp || clientKey)}</span>
         </div>`
      : `<span style="color: var(--color-warm-granite);">—</span>`;

    return `
      <tr data-client="${escapeHtml(clientKey)}">
        <td style="text-align: center; font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-warm-granite);">${idx + 1}</td>
        <td class="mono" style="font-size: 11px; color: var(--color-pale-stone); white-space: nowrap;">${escapeHtml(e.timestampLocal || '-')}</td>
        <td style="white-space: nowrap;">${clientPill}</td>
        <td class="mono" style="font-weight: 600; color: var(--color-bone);">${serverPortDisplay}</td>
        <td>
          <div style="font-size: 12px; color: var(--color-bone);">${escapeHtml(e.displayName || e.serviceName)}</div>
        </td>
        <td style="text-align: center;">${actionBadge}</td>
        <td style="text-align: center;">${statusBadge}</td>
        <td style="text-align: right;" class="mono" style="font-size: 11px;">${durationSec}</td>
        <td style="font-size: 11px; color: ${resultColor};">
          ${escapeHtml(resultText)}
        </td>
      </tr>
    `;
  }).join('');

  updateAuditSortHeaders();

  // Wire interactive hover & quick filter click for client pills
  auditTableBody.querySelectorAll('.audit-client-pill').forEach(pill => {
    const key = pill.dataset.client;
    if (!key || key === '—') return;

    pill.addEventListener('mouseenter', () => {
      const pal = getAuditClientPalette(key);
      const rows = auditTableBody.querySelectorAll('tr');
      rows.forEach(tr => {
        if (tr.dataset.client === key) {
          tr.classList.add('audit-row-highlight');
          tr.style.setProperty('--highlight-color', pal.color);
        }
      });
    });

    pill.addEventListener('mouseleave', () => {
      const rows = auditTableBody.querySelectorAll('tr');
      rows.forEach(tr => {
        if (tr.dataset.client === key) {
          tr.classList.remove('audit-row-highlight');
          tr.style.removeProperty('--highlight-color');
        }
      });
    });

    pill.addEventListener('click', (ev) => {
      ev.stopPropagation();
      toggleAuditClientFilter(key);
    });
  });
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

// ============================================================================
// SECRET RESTORE DATABASES CONSOLE LOGIC
// ============================================================================

let currentRestoreTarget = null;
let currentTimelinePoints = [];
const restorePollingTimers = new Map();

function formatRestoreBytes(bytes) {
  if (!bytes || bytes <= 0) return '0 Б';
  const k = 1024;
  const sizes = ['Б', 'КБ', 'МБ', 'ГБ', 'ТБ'];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return (bytes / Math.pow(k, i)).toFixed(1) + ' ' + sizes[i];
}

function formatEstimatedTime(est) {
  if (!est) return 'оценка...';
  if (typeof est === 'string') {
    const parts = est.split(':');
    if (parts.length >= 3) {
      const h = parseInt(parts[0], 10) || 0;
      const m = parseInt(parts[1], 10) || 0;
      const s = Math.round(parseFloat(parts[2])) || 0;
      if (h > 0) return `~${h} ч ${m} мин`;
      if (m > 0) return `~${m} мин ${s} с`;
      if (s > 0) return `~${s} с`;
    }
  }
  return 'оценка...';
}

function matchesWildcardMask(pattern, str) {
  if (!pattern || !str) return false;
  pattern = pattern.trim();
  str = str.trim();
  if (pattern.toLowerCase() === str.toLowerCase()) return true;
  if (pattern.includes('*') || pattern.includes('?')) {
    try {
      const escaped = pattern.replace(/[.+^${}()|[\]\\]/g, '\\$&').replace(/\*/g, '.*').replace(/\?/g, '.');
      return new RegExp('^' + escaped + '$', 'i').test(str);
    } catch {
      return false;
    }
  }
  return false;
}

async function loadRestoreData(force = false) {
  if (!restoreTableBody) return;
  if (!force && state.restoreItems && state.restoreItems.length > 0) {
    renderRestoreTable();
    return;
  }

  restoreTableBody.innerHTML = `
    <tr>
      <td colspan="7" style="text-align: center; padding: 30px;">
        <div class="loading-container">
          <span class="spinner spinner-lg"></span>
          <span>Загрузка DEV-баз для восстановления...</span>
        </div>
      </td>
    </tr>
  `;

  try {
    // 1. Fetch excluded database patterns from restore config
    let excludedMasks = [
      'master', 'model', 'msdb', 'tempdb', 'distribution', 'susdb',
      'dbadb', 'dbasqlperformance', 'reportserver*', 'reportservertempdb'
    ];
    try {
      const cfgRes = await fetch('/api/restore/config');
      if (cfgRes.ok) {
        const cfgData = await cfgRes.json();
        if (Array.isArray(cfgData.excludedDatabases) && cfgData.excludedDatabases.length > 0) {
          excludedMasks = [...new Set([...excludedMasks, ...cfgData.excludedDatabases])];
        }
      }
    } catch {}

    // 2. Load DEV databases from API
    const res = await fetch('/api/databases?environment=DEV&pageSize=5000');
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    const data = await res.json();
    state.restoreItems = (data.items || []).filter(b => {
      const env = (b.environment || '').toUpperCase();
      if (env !== 'DEV') return false;
      const name = b.name || '';
      const sqlDb = b.sqlDbName || '';
      for (const mask of excludedMasks) {
        if (matchesWildcardMask(mask, name) || matchesWildcardMask(mask, sqlDb)) {
          return false;
        }
      }
      return true;
    });

    // 2. Fetch active restore operations from backend
    try {
      const activeRes = await fetch('/api/restore/active');
      if (activeRes.ok) {
        const activeList = await activeRes.json();
        if (Array.isArray(activeList)) {
          activeList.forEach(op => {
            const opKey = `${op.targetServer}:${op.targetDatabase}`;
            state.activeRestoreOperations.set(opKey, op);
            const running = op.status === 'Running' || op.status === 'Queued' || op.status === 'RUNNING';
            if (running && !restorePollingTimers.has(op.operationId)) {
              pollRestoreOperation(op.operationId, opKey);
            }
          });
        }
      }
    } catch (err) {
      console.warn('Не удалось загрузить активные операции восстановления:', err);
    }

    renderRestoreTable();
    renderPagination();
  } catch (err) {
    restoreTableBody.innerHTML = `
      <tr>
        <td colspan="7" style="text-align: center; padding: 30px; color: var(--color-signal-orange);">
          Ошибка загрузки DEV-баз: ${escapeHtml(err.message)}
        </td>
      </tr>
    `;
  }
}

function filterRestore() {
  let list = state.restoreItems || [];
  const search = (restoreSearchInput ? restoreSearchInput.value : '').trim().toLowerCase();
  let filtered = list;
  if (search) {
    filtered = list.filter(b => {
      return (b.name && b.name.toLowerCase().includes(search)) ||
             (b.cluster && b.cluster.toLowerCase().includes(search)) ||
             (b.sql && b.sql.toLowerCase().includes(search)) ||
             (b.sqlHost && b.sqlHost.toLowerCase().includes(search)) ||
             (b.sqlDbName && b.sqlDbName.toLowerCase().includes(search));
    });
  }

  const sortBy = state.restoreSortBy || 'name';
  const sortDir = state.restoreSortDir || 'asc';
  return [...filtered].sort((a, b) => {
    let valA = '';
    let valB = '';
    if (sortBy === 'cluster') {
      valA = a.cluster ?? '';
      valB = b.cluster ?? '';
    } else if (sortBy === 'name') {
      valA = a.name ?? '';
      valB = b.name ?? '';
    } else if (sortBy === 'sql') {
      valA = a.sqlDisplay ?? a.sql ?? '';
      valB = b.sqlDisplay ?? b.sql ?? '';
    } else if (sortBy === 'sqldbname') {
      valA = a.sqlDbName ?? '';
      valB = b.sqlDbName ?? '';
    } else if (sortBy === 'env') {
      valA = a.environment ?? '';
      valB = b.environment ?? '';
    }
    valA = valA.toString().toLowerCase();
    valB = valB.toString().toLowerCase();
    return sortDir === 'asc' ? valA.localeCompare(valB, 'ru') : valB.localeCompare(valA, 'ru');
  });
}

function updateRestoreSortHeaders() {
  document.querySelectorAll('th.sortable-restore').forEach(th => {
    const field = th.dataset.sort;
    const icon = th.querySelector('.sort-icon');
    if (field === state.restoreSortBy) {
      th.classList.add('sorted');
      if (icon) icon.textContent = state.restoreSortDir === 'asc' ? '▲' : '▼';
    } else {
      th.classList.remove('sorted');
      if (icon) icon.textContent = '⇅';
    }
  });
}

function renderRestoreTable() {
  if (!restoreTableBody) return;
  const list = filterRestore();
  if (list.length === 0) {
    restoreTableBody.innerHTML = `
      <tr>
        <td colspan="7" style="text-align: center; padding: 30px; color: var(--color-warm-granite);">
          DEV-базы данных не найдены
        </td>
      </tr>
    `;
    updateRestoreSortHeaders();
    return;
  }

  restoreTableBody.innerHTML = list.map((b, idx) => {
    const opKey = `${b.sql || ''}:${b.sqlDbName || ''}`;
    const op = state.activeRestoreOperations.get(opKey);
    const safeId = opKey.replace(/[^a-zA-Z0-9_-]/g, '_');

    const isRunning = op && (op.status === 'Running' || op.status === 'Queued' || op.status === 0 || op.status === 1 || op.status === 'RUNNING');
    const isCompleted = op && (op.status === 'Completed' || op.status === 2 || op.status === 'COMPLETED');
    const isFailed = op && (op.status === 'Failed' || op.status === 3 || op.status === 'FAILED');

    let actionContent = '';
    if (isRunning) {
      const p = Math.max(5, Math.min(100, Math.round(op.percentComplete || 0)));
      const stageText = op.stage || op.currentStage || 'Восстановление...';
      let timerStr = 'осталось: оценка';
      if (typeof op.estimatedSecondsRemaining === 'number' && op.estimatedSecondsRemaining > 0) {
        const m = Math.floor(op.estimatedSecondsRemaining / 60);
        const s = op.estimatedSecondsRemaining % 60;
        timerStr = m > 0 ? `осталось ~${m} мин ${s} с` : `осталось ~${s} с`;
      } else if (op.estimatedTimeRemaining) {
        timerStr = `осталось ${formatEstimatedTime(op.estimatedTimeRemaining)}`;
      }
      actionContent = `
        <div class="restore-progress-container" id="restoreProgress_${safeId}">
          <div class="restore-progress-meta">
            <span class="restore-progress-stage" title="${escapeHtml(stageText)}">${escapeHtml(stageText)}</span>
            <span class="restore-progress-percent">${p}%</span>
          </div>
          <div class="restore-progress-bar-bg">
            <div class="restore-progress-bar-fill" style="width: ${p}%;"></div>
          </div>
          <span class="restore-progress-timer">${timerStr}</span>
        </div>
      `;
    } else if (isCompleted) {
      // Warnings (skipped users, shrink notes) are kept in the audit log, not in the table
      actionContent = `
        <div style="display: flex; align-items: center; justify-content: center; gap: 8px;">
          <span class="restore-btn-success" title="${escapeHtml(op.stage || '')}">✓ Восстановлено</span>
          <button class="btn btn-ghost btn-sm btn-open-restore" data-index="${idx}" title="Восстановить повторно">⟲</button>
        </div>
      `;
    } else if (isFailed) {
      actionContent = `
        <div style="display: flex; align-items: center; justify-content: center; gap: 8px;">
          <span class="restore-btn-failed" title="${escapeHtml(op.errorMessage || '')}">✕ Ошибка</span>
          <button class="btn btn-accent btn-sm btn-open-restore" data-index="${idx}">Повторить</button>
        </div>
        ${op.errorMessage ? `<div style="margin-top: 4px; font-size: 10.5px; color: var(--color-signal-orange); text-align: left; max-width: 420px; white-space: normal;">${escapeHtml(op.errorMessage)}</div>` : ''}
      `;
    } else {
      actionContent = `
        <button class="btn btn-accent btn-sm btn-open-restore" data-index="${idx}">
          ⟲ Восстановить
        </button>
      `;
    }

    return `
      <tr>
        <td style="text-align: center; font-family: var(--font-geist-mono); font-size: 11px; color: var(--color-warm-granite);">${idx + 1}</td>
        <td style="text-align: center;"><span class="badge badge-dev">DEV</span></td>
        <td><code>${escapeHtml(b.cluster || '—')}</code></td>
        <td><strong style="color: var(--color-bone); font-weight: 500;" title="${escapeHtml(b.name)}">${escapeHtml(b.name)}</strong></td>
        <td><code>${escapeHtml(b.sqlDisplay || b.sql || '—')}</code></td>
        <td><span class="mono" style="color: var(--color-bone);">${escapeHtml(b.sqlDbName || '—')}</span></td>
        <td style="text-align: center;">${actionContent}</td>
      </tr>
    `;
  }).join('');

  updateRestoreSortHeaders();
}

const RU_MONTHS = [
  'Январь', 'Февраль', 'Март', 'Апрель', 'Май', 'Июнь',
  'Июль', 'Август', 'Сентябрь', 'Октябрь', 'Ноябрь', 'Декабрь'
];

let pointsByDateMap = new Map();
let currentCalYear = new Date().getFullYear();
let currentCalMonth = new Date().getMonth();
let selectedRestoreDateStr = null;
let selectedRestorePointId = null;

function formatRuDateDisplay(dateStr) {
  if (!dateStr) return '—';
  const parts = dateStr.split('-');
  if (parts.length === 3) {
    return `${parts[2]}.${parts[1]}.${parts[0]}`;
  }
  return dateStr;
}

async function loadRestoreSources(targetDbNameToMatch) {
  if (!restoreSourceSelect) return;
  restoreSourceSelect.innerHTML = '<option value="">Загрузка каталогов архива...</option>';

  try {
    if (!state.restoreSources || state.restoreSources.length === 0) {
      const res = await fetch('/api/restore/sources');
      if (!res.ok) throw new Error(`HTTP ${res.status}`);
      const rawSources = await res.json() || [];
      const sysDbs = new Set([
        'master', 'model', 'msdb', 'tempdb', 'distribution', 'susdb',
        'dbadb', 'dbasqlperformance', 'reportserver', 'reportservertempdb'
      ]);
      state.restoreSources = rawSources.filter(s => {
        const db = (s.database || s.Database || s.databaseName || '').toLowerCase();
        const srv = (s.server || s.Server || s.serverName || '').toUpperCase();
        if (sysDbs.has(db) || db.startsWith('reportserver')) return false;
        if (srv.includes('MANZANA') || srv.includes('CDWH')) return false;
        return true;
      });
    }

    if (state.restoreSources.length === 0) {
      restoreSourceSelect.innerHTML = '<option value="">Каталоги бэкапов не обнаружены</option>';
      return;
    }

    let matchOptionVal = '';
    const cleanTargetName = (targetDbNameToMatch || '').trim().toLowerCase();
    for (const s of state.restoreSources) {
      const { db, value } = restoreSourceParts(s);
      const dbLower = db.toLowerCase();
      if (cleanTargetName && (
        dbLower === cleanTargetName ||
        dbLower.replace(/_dev$/, '') === cleanTargetName.replace(/_dev$/, '') ||
        cleanTargetName.replace(/_dev$/, '') === dbLower
      )) {
        matchOptionVal = value;
        break;
      }
    }

    renderRestoreSourceOptions(restoreSourceSearch ? restoreSourceSearch.value : '', matchOptionVal);

    if (matchOptionVal) {
      await onRestoreSourceChanged();
    }
  } catch (err) {
    restoreSourceSelect.innerHTML = `<option value="">Ошибка загрузки каталогов: ${escapeHtml(err.message)}</option>`;
  }
}

function restoreSourceParts(s) {
  const srv = s.server || s.Server || s.serverName || '';
  const db = s.database || s.Database || s.databaseName || '';
  return { srv, db, value: `${srv}/${db}`, title: s.displayName || s.DisplayName || `${srv} / ${db}` };
}

// Fills the source list filtered by the search text; the selected catalog stays in the list even if filtered out
function renderRestoreSourceOptions(filterText, selectedValue) {
  if (!restoreSourceSelect) return 0;
  const q = (filterText || '').trim().toLowerCase();
  const all = state.restoreSources || [];
  const matches = q
    ? all.filter(s => { const p = restoreSourceParts(s); return `${p.srv} ${p.db}`.toLowerCase().includes(q); })
    : all;

  const selected = selectedValue ? all.find(s => restoreSourceParts(s).value === selectedValue) : null;
  const list = selected && !matches.includes(selected) ? [selected, ...matches] : matches;

  const placeholder = q
    ? (matches.length ? `-- Найдено: ${matches.length} из ${all.length} --` : '-- Ничего не найдено --')
    : `-- Выберите каталог (${all.length}) --`;

  restoreSourceSelect.innerHTML = `<option value="">${placeholder}</option>` + list.map(s => {
    const p = restoreSourceParts(s);
    return `<option value="${escapeHtml(p.value)}">${escapeHtml(p.title)}</option>`;
  }).join('');
  restoreSourceSelect.value = selected ? selectedValue : '';
  return matches.length;
}

const restoreSourceSearch = document.getElementById('restoreSourceSearch');
if (restoreSourceSearch) {
  restoreSourceSearch.addEventListener('input', () => {
    const current = restoreSourceSelect ? restoreSourceSelect.value : '';
    renderRestoreSourceOptions(restoreSourceSearch.value, current);
  });

  // Enter picks the first match: quick keyboard flow "type name -> Enter"
  restoreSourceSearch.addEventListener('keydown', async (e) => {
    if (e.key !== 'Enter') return;
    e.preventDefault();
    const q = restoreSourceSearch.value.trim().toLowerCase();
    const first = (state.restoreSources || []).find(s => {
      const p = restoreSourceParts(s);
      return !q || `${p.srv} ${p.db}`.toLowerCase().includes(q);
    });
    if (!first || !restoreSourceSelect) return;
    const value = restoreSourceParts(first).value;
    if (restoreSourceSelect.value === value) return;
    renderRestoreSourceOptions(restoreSourceSearch.value, value);
    await onRestoreSourceChanged();
  });
}

async function onRestoreSourceChanged() {
  const val = restoreSourceSelect ? restoreSourceSelect.value : '';
  const timelineControls = document.getElementById('restoreTimelineControls');
  const emptyHint = document.getElementById('restorePointEmpty');
  const summaryBox = document.getElementById('restoreSelectedSummary');

  if (!val) {
    if (btnStartRestore) btnStartRestore.disabled = true;
    if (timelineControls) timelineControls.style.display = 'none';
    if (emptyHint) emptyHint.style.display = 'none';
    if (summaryBox) summaryBox.style.display = 'none';
    selectedRestorePointId = null;
    selectedRestoreDateStr = null;
    pointsByDateMap.clear();
    return;
  }

  const slashIdx = val.indexOf('/');
  if (slashIdx === -1) return;
  const server = val.substring(0, slashIdx);
  const database = val.substring(slashIdx + 1);

  if (restoreTimelineLoading) restoreTimelineLoading.style.display = 'block';
  if (timelineControls) timelineControls.style.display = 'none';
  if (emptyHint) emptyHint.style.display = 'none';
  if (summaryBox) summaryBox.style.display = 'none';
  if (btnStartRestore) btnStartRestore.disabled = true;

  try {
    const res = await fetch(`/api/restore/timeline?server=${encodeURIComponent(server)}&database=${encodeURIComponent(database)}`);
    if (!res.ok) throw new Error(`HTTP ${res.status}`);
    currentTimelinePoints = await res.json() || [];

    pointsByDateMap = new Map();
    currentTimelinePoints.forEach(p => {
      const dateKey = p.date || (p.backupDate ? p.backupDate.slice(0, 10) : '');
      if (!dateKey) return;
      if (!pointsByDateMap.has(dateKey)) pointsByDateMap.set(dateKey, []);
      pointsByDateMap.get(dateKey).push(p);
    });

    if (pointsByDateMap.size === 0) {
      if (emptyHint) {
        emptyHint.textContent = 'В выбранном каталоге не найдено файлов бэкапов (.bak / .diff / .trn). Проверка содержимого каталога...';
        emptyHint.style.display = 'block';
        showRestoreCatalogDiagnostics(server, database, emptyHint);
      }
      if (timelineControls) timelineControls.style.display = 'none';
      if (btnStartRestore) btnStartRestore.disabled = true;
      return;
    }

    if (timelineControls) timelineControls.style.display = 'grid';

    // Sort dates descending
    const sortedDates = Array.from(pointsByDateMap.keys()).sort().reverse();
    const latestDate = sortedDates[0];

    const [y, m] = latestDate.split('-').map(Number);
    currentCalYear = y;
    currentCalMonth = m - 1; // 0-based month
    selectedRestoreDateStr = latestDate;
    selectedRestorePointId = null;

    renderRestoreCalendar();
    renderRestorePointsForDate(latestDate);
  } catch (err) {
    showToast(`Ошибка загрузки точек бэкапов: ${err.message}`, 'error');
  } finally {
    if (restoreTimelineLoading) restoreTimelineLoading.style.display = 'none';
  }
}

// Explains an empty timeline: what the service account actually sees in the catalog
async function showRestoreCatalogDiagnostics(server, database, target) {
  const requestedFor = `${server}/${database}`;
  try {
    const res = await fetch(`/api/restore/diagnose?server=${encodeURIComponent(server)}&database=${encodeURIComponent(database)}`);
    const d = await res.json().catch(() => ({}));
    if (!restoreSourceSelect || restoreSourceSelect.value !== requestedFor) return;
    if (!res.ok) throw new Error(d.error || `HTTP ${res.status}`);

    const lines = [];
    lines.push(`<div>В выбранном каталоге не найдено файлов бэкапов (.bak / .diff / .trn).</div>`);
    lines.push(`<div style="margin-top: 6px; color: var(--color-warm-granite);">Каталог: <code>${escapeHtml(d.path)}</code><br>Учётная запись службы: <code>${escapeHtml(d.account)}</code></div>`);

    if (!d.exists) {
      lines.push(`<div style="margin-top: 6px;">${escapeHtml(d.error || 'Каталог недоступен')}</div>`);
    } else {
      const ext = Object.entries(d.extensions || {}).sort((a, b) => b[1] - a[1]).map(([k, v]) => `${k}: ${v}`).join(', ');
      lines.push(`<div style="margin-top: 6px;">Файлов всего: ${d.totalFiles}${ext ? ` (${escapeHtml(ext)})` : ''}. Поиск ведётся на глубину ${d.searchDepth} уровня.</div>`);
      if ((d.subDirectories || []).length) {
        lines.push(`<div style="margin-top: 4px;">Подкаталоги: <code>${d.subDirectories.map(escapeHtml).join('</code>, <code>')}</code></div>`);
      }
      if ((d.sampleFiles || []).length) {
        lines.push(`<div style="margin-top: 4px;">Примеры файлов: <code>${d.sampleFiles.map(escapeHtml).join('</code>, <code>')}</code></div>`);
      }
      if ((d.accessErrors || []).length) {
        lines.push(`<div style="margin-top: 4px;">Нет доступа: ${d.accessErrors.map(escapeHtml).join('; ')}</div>`);
      }
    }
    target.innerHTML = lines.join('');
  } catch (err) {
    if (restoreSourceSelect && restoreSourceSelect.value === requestedFor) {
      target.textContent = `В выбранном каталоге не найдено файлов бэкапов. Диагностика не удалась: ${err.message}`;
    }
  }
}

function renderRestoreCalendar() {
  const calMonthLabel = document.getElementById('calMonthLabel');
  const calDaysGrid = document.getElementById('calDaysGrid');
  if (!calMonthLabel || !calDaysGrid) return;

  calMonthLabel.textContent = `${RU_MONTHS[currentCalMonth]} ${currentCalYear}`;

  const daysInMonth = new Date(currentCalYear, currentCalMonth + 1, 0).getDate();
  const firstDayOfWeek = (new Date(currentCalYear, currentCalMonth, 1).getDay() + 6) % 7; // Mon=0, Sun=6
  const daysInPrevMonth = new Date(currentCalYear, currentCalMonth, 0).getDate();

  let html = '';

  // Previous month trailing days
  for (let i = firstDayOfWeek - 1; i >= 0; i--) {
    const d = daysInPrevMonth - i;
    html += `<div class="cal-day-cell other-month">${d}</div>`;
  }

  // Current month days
  for (let day = 1; day <= daysInMonth; day++) {
    const mStr = String(currentCalMonth + 1).padStart(2, '0');
    const dStr = String(day).padStart(2, '0');
    const dateKey = `${currentCalYear}-${mStr}-${dStr}`;
    const hasBackups = pointsByDateMap.has(dateKey) && pointsByDateMap.get(dateKey).length > 0;
    const isSelected = dateKey === selectedRestoreDateStr;

    let cellClasses = 'cal-day-cell';
    if (hasBackups) cellClasses += ' has-backups';
    if (isSelected) cellClasses += ' selected';

    const dotHtml = hasBackups ? '<span class="cal-day-dot"></span>' : '';
    const clickAttr = hasBackups ? `data-date="${dateKey}"` : '';

    html += `<button type="button" class="${cellClasses}" ${clickAttr} title="${hasBackups ? `${dateKey}: ${pointsByDateMap.get(dateKey).length} точ.` : ''}">${day}${dotHtml}</button>`;
  }

  // Next month leading days to complete full grid row
  const totalRendered = firstDayOfWeek + daysInMonth;
  const remaining = (7 - (totalRendered % 7)) % 7;
  for (let d = 1; d <= remaining; d++) {
    html += `<div class="cal-day-cell other-month">${d}</div>`;
  }

  calDaysGrid.innerHTML = html;
}

function selectRestoreDate(dateKey) {
  selectedRestoreDateStr = dateKey;
  renderRestoreCalendar();
  renderRestorePointsForDate(dateKey);
}

function renderRestorePointsForDate(dateKey) {
  const pointsDateTitle = document.getElementById('restorePointsDateTitle');
  const pointsCount = document.getElementById('restorePointsCount');
  const pointsList = document.getElementById('restorePointsList');
  if (!pointsList) return;

  const points = pointsByDateMap.get(dateKey) || [];

  if (pointsDateTitle) {
    pointsDateTitle.textContent = `Точки на ${formatRuDateDisplay(dateKey)}`;
  }
  if (pointsCount) {
    pointsCount.textContent = `${points.length} точ.`;
  }

  if (points.length === 0) {
    pointsList.innerHTML = '<div class="restore-empty-hint">На эту дату нет бэкапов</div>';
    selectedRestorePointId = null;
    updateSelectedPointSummary(null);
    if (btnStartRestore) btnStartRestore.disabled = true;
    return;
  }

  // Check if current selected point exists on this date, else pick first (latest)
  let activePoint = points.find(p => p.id === selectedRestorePointId);
  if (!activePoint) {
    activePoint = points[0];
    selectedRestorePointId = activePoint.id;
  }

  pointsList.innerHTML = points.map(p => {
    const isSelected = p.id === selectedRestorePointId;
    const typeStr = (p.type || '').toString().toLowerCase();
    const isFull = typeStr.includes('full') || p.type === 0;
    const isDiff = typeStr.includes('diff') || p.type === 1;
    const typeClass = isFull ? 'point-type-full' : (isDiff ? 'point-type-diff' : 'point-type-log');
    const typeLabel = isFull ? 'FULL' : (isDiff ? 'DIFF' : 'LOG');

    return `
      <div class="restore-point-card ${isSelected ? 'selected' : ''}" data-point-id="${escapeHtml(p.id)}">
        <div class="point-card-left">
          <div class="point-radio-indicator"></div>
          <div class="point-time-val">${escapeHtml(p.time || '--:--:--')}</div>
          <span class="point-type-badge ${typeClass}">${typeLabel}</span>
        </div>
        <div class="point-size-val">${escapeHtml(p.sizeDisplay || '')}</div>
      </div>
    `;
  }).join('');

  updateSelectedPointSummary(activePoint);
  if (btnStartRestore) btnStartRestore.disabled = false;
}

function updateSelectedPointSummary(point) {
  const summaryBox = document.getElementById('restoreSelectedSummary');
  const summaryTime = document.getElementById('summaryPointTime');
  const summaryBadge = document.getElementById('summaryPointBadge');

  if (!summaryBox) return;

  if (!point) {
    summaryBox.style.display = 'none';
    return;
  }

  summaryBox.style.display = 'block';
  if (summaryTime) {
    summaryTime.textContent = `${formatRuDateDisplay(selectedRestoreDateStr)}, ${point.time || ''}`;
  }

  const kind = restorePointKind(point.type);
  if (summaryBadge) {
    summaryBadge.className = 'summary-badge ' + kind.cls;
    summaryBadge.textContent = kind.label;
  }

  loadRestorePlan(point);
}

function restorePointKind(type) {
  const t = (type ?? '').toString().toLowerCase();
  if (t.includes('full') || type === 0) return { cls: 'point-type-full', short: 'FULL', label: 'по полному бэкапу' };
  if (t.includes('diff') || type === 1) return { cls: 'point-type-diff', short: 'DIFF', label: 'по разностному бэкапу' };
  return { cls: 'point-type-log', short: 'LOG', label: 'по журналу транзакций' };
}

let restorePlanSeq = 0;

// Shows which backups will actually be applied for the selected point (same planner as the restore itself)
async function loadRestorePlan(point) {
  const stepsBox = document.getElementById('restorePlanSteps');
  const sourceVal = restoreSourceSelect ? restoreSourceSelect.value : '';
  const slashIdx = sourceVal.indexOf('/');
  if (!stepsBox || slashIdx === -1) return;

  const seq = ++restorePlanSeq;
  stepsBox.innerHTML = '<div style="color: var(--color-warm-granite); font-size: 11px;"><span class="spinner spinner-sm"></span> Расчёт цепочки бэкапов...</div>';

  try {
    const qs = new URLSearchParams({
      server: sourceVal.substring(0, slashIdx),
      database: sourceVal.substring(slashIdx + 1),
      pointId: point.id
    });
    const res = await fetch(`/api/restore/plan?${qs}`);
    const plan = await res.json().catch(() => ({}));
    if (seq !== restorePlanSeq) return;
    if (!res.ok) throw new Error(plan.error || `HTTP ${res.status}`);

    if (plan.error) {
      stepsBox.innerHTML = `<div class="restore-plan-error">${escapeHtml(plan.error)}</div>`;
      if (btnStartRestore) btnStartRestore.disabled = true;
      return;
    }

    const titles = {
      FULL: 'Полный бэкап',
      DIFF: 'Разностный бэкап — изменения с момента полного',
      LOG: 'Журналы транзакций — изменения до выбранной точки'
    };

    const rows = (plan.steps || []).map((s, i) => {
      const kind = restorePointKind(s.type);
      const when = s.from === s.to ? s.from : `${s.from} → ${s.to}`;
      const count = kind.short === 'LOG' ? ` · ${s.backupCount} шт.` : '';
      return `
        <div class="restore-plan-step" title="${escapeHtml(titles[kind.short])}">
          <span class="restore-plan-step-num">${i + 1}.</span>
          <span class="point-type-badge ${kind.cls}" style="text-align: center;">${kind.short}</span>
          <span class="restore-plan-step-when">${escapeHtml(when)}${count}</span>
          <span class="restore-plan-step-size">${escapeHtml(s.sizeDisplay)}</span>
        </div>
      `;
    }).join('');

    stepsBox.innerHTML = `
      ${rows}
      <div class="restore-plan-total">
        <span>Будет применено: ${(plan.steps || []).length} шаг(а), файлов: ${plan.totalFiles}, объём: ${escapeHtml(plan.totalDisplay)}</span>
        <span>Цепочка проверяется по LSN перед запуском</span>
      </div>
    `;
  } catch (err) {
    if (seq !== restorePlanSeq) return;
    stepsBox.innerHTML = `<div class="restore-plan-error">Не удалось рассчитать план: ${escapeHtml(err.message)}</div>`;
  }
}

function openRestoreModal(name, cluster, sql, sqlDbName) {
  currentRestoreTarget = { name, cluster, sql, sqlDbName };
  if (restoreSourceSearch) restoreSourceSearch.value = '';
  if (restoreTargetDbName) {
    restoreTargetDbName.textContent = name;
    restoreTargetDbName.title = name;
  }
  if (restoreTargetServer) restoreTargetServer.textContent = sql || '—';
  if (restoreTargetCluster) restoreTargetCluster.textContent = cluster || '—';
  if (btnStartRestore) {
    btnStartRestore.disabled = true;
    btnStartRestore.textContent = 'Начать восстановление';
  }
  const emptyHint = document.getElementById('restorePointEmpty');
  const timelineControls = document.getElementById('restoreTimelineControls');
  const summaryBox = document.getElementById('restoreSelectedSummary');

  if (emptyHint) emptyHint.style.display = 'none';
  if (timelineControls) timelineControls.style.display = 'none';
  if (summaryBox) summaryBox.style.display = 'none';

  if (restoreModal) {
    restoreModal.classList.add('open');
    restoreModal.style.display = 'flex';
  }

  loadRestoreSources(sqlDbName || name);
}

function closeRestoreModal() {
  if (restoreModal) {
    restoreModal.classList.remove('open');
    restoreModal.style.display = 'none';
  }
  currentRestoreTarget = null;
  currentTimelinePoints = [];
  pointsByDateMap.clear();
  selectedRestoreDateStr = null;
  selectedRestorePointId = null;
}

async function startRestoreExecution() {
  if (!currentRestoreTarget || !btnStartRestore) return;
  const sourceVal = restoreSourceSelect ? restoreSourceSelect.value : '';
  if (!sourceVal) return;
  const slashIdx = sourceVal.indexOf('/');
  if (slashIdx === -1) return;
  const srcServer = sourceVal.substring(0, slashIdx);
  const srcDb = sourceVal.substring(slashIdx + 1);
  const pointId = selectedRestorePointId;
  if (!pointId) {
    showToast('Пожалуйста, выберите точку восстановления на календаре.', 'warning');
    return;
  }

  btnStartRestore.disabled = true;
  btnStartRestore.innerHTML = '<span class="spinner spinner-sm"></span> Запуск...';

  let clusterHost = '';
  let clusterPort = 1541;
  if (currentRestoreTarget.cluster) {
    const parts = currentRestoreTarget.cluster.split(':');
    clusterHost = parts[0];
    if (parts.length > 1) {
      clusterPort = parseInt(parts[1], 10) || 1541;
    }
  }

  const payload = {
    targetServer: currentRestoreTarget.sql,
    targetDatabase: currentRestoreTarget.sqlDbName,
    sourceServer: srcServer,
    sourceDatabase: srcDb,
    pointId: pointId,
    denyScheduledJobsIn1C: chkRestoreDenyJobs ? chkRestoreDenyJobs.checked : true,
    setSimpleRecoveryAndShrink: chkRestoreSimpleShrink ? chkRestoreSimpleShrink.checked : true,
    restorePermissions: chkRestorePermissions ? chkRestorePermissions.checked : true,
    clusterHost: clusterHost,
    clusterPort: clusterPort,
    infobaseName: currentRestoreTarget.name
  };

  try {
    const res = await fetch('/api/restore/start', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload)
    });

    if (!res.ok) {
      const errData = await res.json().catch(() => ({ error: `HTTP ${res.status}` }));
      throw new Error(errData.error || `HTTP ${res.status}`);
    }

    const stateData = await res.json();
    // closeRestoreModal() resets currentRestoreTarget, so capture what is needed first
    const target = currentRestoreTarget;
    const opKey = `${target.sql}:${target.sqlDbName}`;
    state.activeRestoreOperations.set(opKey, stateData);

    pollRestoreOperation(stateData.operationId, opKey);
    closeRestoreModal();
    renderRestoreTable();
    showToast(`Восстановление базы ${target.name} запущено`, 'success');
  } catch (err) {
    showToast(`Ошибка запуска восстановления: ${err.message}`, 'error');
    btnStartRestore.disabled = false;
    btnStartRestore.textContent = 'Начать восстановление';
  }
}

function pollRestoreOperation(operationId, opKey) {
  if (restorePollingTimers.has(operationId)) {
    clearInterval(restorePollingTimers.get(operationId));
  }

  const timer = setInterval(async () => {
    try {
      const res = await fetch(`/api/restore/status/${encodeURIComponent(operationId)}`);
      if (!res.ok) {
        clearInterval(timer);
        restorePollingTimers.delete(operationId);
        return;
      }
      const op = await res.json();
      state.activeRestoreOperations.set(opKey, op);

      const isCompleted = op.status === 'Completed' || op.status === 2 || op.status === 'COMPLETED';
      const isFailed = op.status === 'Failed' || op.status === 3 || op.status === 'FAILED';

      const safeId = opKey.replace(/[^a-zA-Z0-9_-]/g, '_');
      const progContainer = document.getElementById(`restoreProgress_${safeId}`);
      if (progContainer) {
        const fill = progContainer.querySelector('.restore-progress-bar-fill');
        const stage = progContainer.querySelector('.restore-progress-stage');
        const percent = progContainer.querySelector('.restore-progress-percent');
        const timerEl = progContainer.querySelector('.restore-progress-timer');
        const p = Math.max(5, Math.min(100, Math.round(op.percentComplete || 0)));
        if (fill) fill.style.width = `${p}%`;
        if (stage) {
          stage.textContent = op.stage || op.currentStage || 'Восстановление...';
          stage.title = stage.textContent;
        }
        if (percent) percent.textContent = `${p}%`;
        if (timerEl) {
          if (typeof op.estimatedSecondsRemaining === 'number' && op.estimatedSecondsRemaining > 0) {
            const m = Math.floor(op.estimatedSecondsRemaining / 60);
            const s = op.estimatedSecondsRemaining % 60;
            timerEl.textContent = m > 0 ? `осталось ~${m} мин ${s} с` : `осталось ~${s} с`;
          } else if (op.estimatedTimeRemaining) {
            timerEl.textContent = `осталось ${formatEstimatedTime(op.estimatedTimeRemaining)}`;
          } else {
            timerEl.textContent = 'осталось: оценка';
          }
        }
      } else {
        renderRestoreTable();
      }

      if (isCompleted) {
        clearInterval(timer);
        restorePollingTimers.delete(operationId);
        showToast(`База ${op.targetDatabase} успешно восстановлена`, 'success');
        renderRestoreTable();
      } else if (isFailed) {
        clearInterval(timer);
        restorePollingTimers.delete(operationId);
        showToast(`Ошибка восстановления базы ${op.targetDatabase}: ${op.errorMessage}`, 'error');
        renderRestoreTable();
      }
    } catch {
      // transient network error, keep polling
    }
  }, 2000);

  restorePollingTimers.set(operationId, timer);
}

// Event Listeners for Restore View
if (restoreTableBody) {
  restoreTableBody.addEventListener('click', (e) => {
    const btn = e.target.closest('.btn-open-restore');
    if (!btn) return;
    const idx = parseInt(btn.dataset.index, 10);
    const list = filterRestore();
    const item = list[idx];
    if (item) {
      openRestoreModal(item.name, item.cluster, item.sql, item.sqlDbName);
    }
  });
}

if (restoreModal) {
  restoreModal.addEventListener('click', (e) => {
    if (e.target === restoreModal) closeRestoreModal();
  });
}

document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape' && restoreModal && restoreModal.classList.contains('open')) {
    closeRestoreModal();
  }
});

if (restoreModalClose) restoreModalClose.addEventListener('click', closeRestoreModal);
if (btnCancelRestore) btnCancelRestore.addEventListener('click', closeRestoreModal);
if (restoreSourceSelect) restoreSourceSelect.addEventListener('change', onRestoreSourceChanged);

const calPrevBtn = document.getElementById('calPrevMonth');
if (calPrevBtn) {
  calPrevBtn.addEventListener('click', () => {
    if (currentCalMonth === 0) {
      currentCalMonth = 11;
      currentCalYear--;
    } else {
      currentCalMonth--;
    }
    renderRestoreCalendar();
  });
}

const calNextBtn = document.getElementById('calNextMonth');
if (calNextBtn) {
  calNextBtn.addEventListener('click', () => {
    if (currentCalMonth === 11) {
      currentCalMonth = 0;
      currentCalYear++;
    } else {
      currentCalMonth++;
    }
    renderRestoreCalendar();
  });
}

const calDaysGrid = document.getElementById('calDaysGrid');
if (calDaysGrid) {
  calDaysGrid.addEventListener('click', (e) => {
    const dayBtn = e.target.closest('.cal-day-cell.has-backups');
    if (!dayBtn) return;
    const dateKey = dayBtn.dataset.date;
    if (dateKey) selectRestoreDate(dateKey);
  });
}

const restorePointsList = document.getElementById('restorePointsList');
if (restorePointsList) {
  restorePointsList.addEventListener('click', (e) => {
    if (hasActiveTextSelection()) return;
    const card = e.target.closest('.restore-point-card');
    if (!card) return;
    const pointId = card.dataset.pointId;
    const points = pointsByDateMap.get(selectedRestoreDateStr) || [];
    const pt = points.find(p => p.id === pointId);
    if (pt) {
      selectedRestorePointId = pt.id;
      restorePointsList.querySelectorAll('.restore-point-card').forEach(c => {
        c.classList.toggle('selected', c.dataset.pointId === pt.id);
      });
      updateSelectedPointSummary(pt);
      if (btnStartRestore) btnStartRestore.disabled = false;
    }
  });
}

if (btnStartRestore) btnStartRestore.addEventListener('click', startRestoreExecution);
if (restoreSearchInput) restoreSearchInput.addEventListener('input', () => renderRestoreTable());
if (btnRefreshRestore) {
  btnRefreshRestore.addEventListener('click', () => {
    sendConsoleAuditEvent('RESTORE', 'REFRESH_RESTORE_LIST');
    loadRestoreData(true);
  });
}

document.querySelectorAll('th.sortable-restore').forEach(th => {
  th.addEventListener('click', () => {
    if (isColumnResizing) return;
    const field = th.dataset.sort;
    if (state.restoreSortBy === field) {
      state.restoreSortDir = state.restoreSortDir === 'asc' ? 'desc' : 'asc';
    } else {
      state.restoreSortBy = field;
      state.restoreSortDir = 'asc';
    }
    renderRestoreTable();
  });
});

window.openRestoreModal = openRestoreModal;
window.closeRestoreModal = closeRestoreModal;



// ==========================================
// Same-value highlight across all tables
// ==========================================
// Hovering a cell highlights cells with the same value in the same column of the same table
// (platform version, cluster, SQL server...). One delegated listener serves every table,
// including modal tables and tables that are re-rendered.
(function initSameValueHighlight() {
  const ROW_CLASS = 'hl-same-row';
  const CELL_CLASS = 'hl-same-cell';
  const MAX_ROWS = 5000;
  let active = null;

  function cellValue(td) {
    const raw = td.dataset && td.dataset.hl !== undefined ? td.dataset.hl : td.textContent;
    return (raw || '').replace(/\s+/g, ' ').trim();
  }

  function clearHighlight() {
    if (!active) return;
    active.cells.forEach(c => {
      c.classList.remove(CELL_CLASS);
      if (c.parentElement) c.parentElement.classList.remove(ROW_CLASS);
    });
    active = null;
  }

  function isIgnoredCell(td, value) {
    if (!value || value === '—' || value === '-') return true;
    if (td.hasAttribute('colspan')) return true;
    // action / selection cells: buttons and checkboxes would light up every row
    if (td.querySelector('button, input, select')) return true;
    const tr = td.parentElement;
    return !tr || tr.classList.contains('no-hover');
  }

  document.addEventListener('mouseover', (e) => {
    const td = e.target && e.target.closest ? e.target.closest('td') : null;
    const tbody = td ? td.closest('tbody') : null;
    if (!td || !tbody) {
      clearHighlight();
      return;
    }

    const table = td.closest('table');
    const col = td.cellIndex;
    const value = cellValue(td);
    if (active && active.table === table && active.col === col && active.value === value) return;

    clearHighlight();
    if (isIgnoredCell(td, value)) return;

    const rows = tbody.rows;
    if (rows.length > MAX_ROWS) return;

    const matches = [];
    for (const row of rows) {
      const c = row.cells[col];
      if (c && !c.hasAttribute('colspan') && cellValue(c) === value) matches.push(c);
    }
    // Only the hovered cell itself: nothing to relate
    if (matches.length < 2) return;

    matches.forEach(c => {
      c.classList.add(CELL_CLASS);
      c.parentElement.classList.add(ROW_CLASS);
    });
    active = { table, col, value, cells: matches };
  });

  // Leaving the window must not leave rows lit
  document.addEventListener('mouseleave', clearHighlight);
})();
