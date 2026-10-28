import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, TestRequest, provideHttpClientTesting } from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { App } from './app';
import { AppModule } from './app-module';
import { AuditEntry, ImportRunSummary, PagedResult, Transaction } from './transactions-api';

const tx = (overrides: Partial<Transaction> = {}): Transaction => ({
  id: 1,
  sourceSystem: 'Store Uptown',
  transactionId: 100001,
  locationCode: 'UPTOWN',
  productName: 'Wireless Mouse',
  amount: 18.5,
  transactionTime: '2026-02-27T08:15:30Z',
  status: 'Active',
  ...overrides,
});

const page = (items: Transaction[], totalCount = items.length, pageNumber = 1, pageSize = 25): PagedResult<Transaction> => ({
  items,
  page: pageNumber,
  pageSize,
  totalCount,
});

const summary: ImportRunSummary = {
  runId: 7,
  status: 'PartiallyFailed',
  startedAt: '2026-02-27T09:00:00Z',
  completedAt: '2026-02-27T09:00:01Z',
  finalized: 2,
  sources: [
    { sourceSystem: 'Store Uptown', status: 'Succeeded', error: null, fetched: 4, inserted: 3, updated: 1, revoked: 0, reinstated: 0, rejected: 0, ignored: 0, revocationSkipped: false },
    { sourceSystem: 'Online', status: 'Failed', error: 'source down', fetched: 0, inserted: 0, updated: 0, revoked: 0, reinstated: 0, rejected: 0, ignored: 0, revocationSkipped: false },
  ],
};

describe('App', () => {
  let fixture: ComponentFixture<App>;
  let http: HttpTestingController;

  beforeEach(async () => {
    await TestBed.configureTestingModule({
      // App is compiled in AppModule's scope (pipes come from BrowserModule), so use the real module.
      imports: [AppModule],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    }).compileComponents();

    fixture = TestBed.createComponent(App);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  const element = () => fixture.nativeElement as HTMLElement;
  const text = () => element().textContent ?? '';
  const query = (test: string) => element().querySelector<HTMLElement>(`[data-test="${test}"]`);
  const render = () => fixture.detectChanges();

  /** Expects exactly one list request and checks its full query string. */
  function expectList(params: Record<string, string>): TestRequest {
    const request = http.expectOne((r) => r.method === 'GET' && r.url === '/api/transactions');
    const sent = Object.fromEntries(request.request.params.keys().map((k) => [k, request.request.params.get(k)]));
    expect(sent).toEqual(params);
    return request;
  }

  function select(test: string, value: string): void {
    const selectElement = query(test) as HTMLSelectElement;
    selectElement.value = value;
    selectElement.dispatchEvent(new Event('change'));
    render();
  }

  function click(target: HTMLElement | null): void {
    target!.click();
    render();
  }

  function loadFirstPage(result: PagedResult<Transaction> = page([tx()])): void {
    render();
    expectList({ page: '1', pageSize: '25' }).flush(result);
    render();
  }

  it('requests the first page and renders rows with 2-decimal amounts and UTC times', () => {
    render();
    expect(query('loading')).not.toBeNull();

    expectList({ page: '1', pageSize: '25' }).flush(page([tx(), tx({ id: 2, amount: -59.9, status: 'Finalized' })]));
    render();

    expect(element().querySelectorAll('[data-test="row"]').length).toBe(2);
    expect(text()).toContain('Transaction Time (UTC)');
    expect(text()).toContain('Wireless Mouse');
    expect(text()).toContain('18.50');
    expect(text()).toContain('-59.90');
    expect(text()).toContain('2026-02-27 08:15:30');
    expect(query('page-info')!.textContent).toContain('Page 1 of 1');
  });

  it('sends paging and filters to the API and returns to page 1 when a filter changes', () => {
    loadFirstPage(page([tx()], 60));

    click(query('next'));
    expectList({ page: '2', pageSize: '25' }).flush(page([tx()], 60, 2));
    render();
    expect(query('page-info')!.textContent).toContain('Page 2 of 3');

    select('source', 'Online');
    expectList({ page: '1', pageSize: '25', source: 'Online' }).flush(page([tx()], 1));
    render();

    select('status', 'Revoked');
    expectList({ page: '1', pageSize: '25', source: 'Online', status: 'Revoked' }).flush(page([]));
    render();

    select('page-size', '50');
    expectList({ page: '1', pageSize: '50', source: 'Online', status: 'Revoked' }).flush(page([], 0, 1, 50));
    render();

    select('source', '');
    expectList({ page: '1', pageSize: '50', status: 'Revoked' }).flush(page([], 0, 1, 50));
  });

  it('shows the empty state', () => {
    loadFirstPage(page([]));

    expect(query('empty')).not.toBeNull();
    expect(query('row')).toBeNull();
  });

  it('shows an error with a retry that requests again', () => {
    render();
    expectList({ page: '1', pageSize: '25' }).flush(null, { status: 500, statusText: 'Server Error' });
    render();

    expect(query('load-error')!.textContent).toContain('Could not load transactions.');

    click(query('retry'));
    expectList({ page: '1', pageSize: '25' }).flush(page([tx()]));
    render();
    expect(query('load-error')).toBeNull();
    expect(query('row')).not.toBeNull();
  });

  it('import success shows the run summary and refreshes the table', () => {
    loadFirstPage();

    click(query('import'));
    expect((query('import') as HTMLButtonElement).disabled).toBe(true);
    http.expectOne({ method: 'POST', url: '/api/import' }).flush(summary);
    render();

    const outcome = query('import-outcome')!.textContent!;
    expect(outcome).toContain('Import run 7');
    expect(outcome).toContain('PartiallyFailed');
    expect(outcome).toContain('2 finalized');
    expect(outcome).toContain('source down');
    expect((query('import') as HTMLButtonElement).disabled).toBe(false);

    expectList({ page: '1', pageSize: '25' }).flush(page([tx(), tx({ id: 2 })]));
    render();
    expect(element().querySelectorAll('[data-test="row"]').length).toBe(2);
  });

  it('import 409 shows the server message and refreshes the table', () => {
    loadFirstPage();

    click(query('import'));
    http.expectOne({ method: 'POST', url: '/api/import' }).flush(
      { status: 409, title: 'An import is already running.', detail: 'Try again when the current import has finished.' },
      { status: 409, statusText: 'Conflict' },
    );
    render();

    const outcome = query('import-outcome')!;
    expect(outcome.classList).toContain('warning');
    expect(outcome.textContent).toContain('An import is already running. Try again when the current import has finished.');
    expectList({ page: '1', pageSize: '25' }).flush(page([tx()]));
  });

  it('clicking a row loads and shows its audit history', () => {
    loadFirstPage(page([tx({ id: 42 })]));

    click(query('row'));
    expect(query('audit')!.textContent).toContain('Loading audit history');

    const entries: AuditEntry[] = [
      { changedAt: '2026-02-27T09:00:00Z', changeType: 'Created', fieldName: null, oldValue: null, newValue: null, importRunId: 1 },
      { changedAt: '2026-02-27T10:00:00Z', changeType: 'Updated', fieldName: 'Amount', oldValue: '18.50', newValue: '19.00', importRunId: 2 },
    ];
    http.expectOne({ method: 'GET', url: '/api/transactions/42/audit' }).flush(entries);
    render();

    const audit = query('audit')!;
    expect(audit.textContent).toContain('Audit: Store Uptown #100001');
    expect(audit.querySelectorAll('[data-test="audit-entry"]').length).toBe(2);
    expect(audit.textContent).toContain('Amount');
    expect(audit.textContent).toContain('19.00');
    expect(element().querySelector('[data-test="row"]')!.classList).toContain('selected');

    click(query('close-audit'));
    expect(query('audit')).toBeNull();
  });
});
