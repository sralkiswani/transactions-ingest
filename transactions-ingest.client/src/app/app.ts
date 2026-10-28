import { HttpErrorResponse } from '@angular/common/http';
import { Component, OnInit, computed, inject, signal } from '@angular/core';
import { Subscription } from 'rxjs';
import {
  AuditEntry,
  ImportRunSummary,
  PagedResult,
  SOURCE_SYSTEMS,
  STATUSES,
  Transaction,
  TransactionStatus,
  TransactionsApi,
} from './transactions-api';

type ImportOutcome =
  | { kind: 'success'; summary: ImportRunSummary }
  | { kind: 'conflict'; message: string }
  | { kind: 'error'; message: string };

@Component({
  selector: 'app-root',
  standalone: false,
  styleUrl: './app.css',
  templateUrl: './app.html',
})
export class App implements OnInit {
  private readonly api = inject(TransactionsApi);

  protected readonly title = signal('Transactions Ingest');
  protected readonly sources = SOURCE_SYSTEMS;
  protected readonly statuses = STATUSES;
  protected readonly pageSizes = [10, 25, 50, 100];

  // Query state; every change reloads from the API.
  protected readonly page = signal(1);
  protected readonly pageSize = signal(25);
  protected readonly source = signal('');
  protected readonly status = signal<TransactionStatus | ''>('');

  protected readonly result = signal<PagedResult<Transaction> | null>(null);
  protected readonly loading = signal(false);
  protected readonly loadError = signal<string | null>(null);
  protected readonly totalPages = computed(() => {
    const result = this.result();
    return result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;
  });

  protected readonly importing = signal(false);
  protected readonly importOutcome = signal<ImportOutcome | null>(null);

  protected readonly selected = signal<Transaction | null>(null);
  protected readonly audit = signal<AuditEntry[] | null>(null);
  protected readonly auditLoading = signal(false);
  protected readonly auditError = signal<string | null>(null);

  private listRequest?: Subscription;
  private auditRequest?: Subscription;

  ngOnInit(): void {
    this.load();
  }

  protected load(): void {
    // Cancel a slower earlier request so its response can't overwrite a newer query's.
    this.listRequest?.unsubscribe();
    this.loading.set(true);
    this.loadError.set(null);
    this.listRequest = this.api
      .list({ page: this.page(), pageSize: this.pageSize(), source: this.source(), status: this.status() })
      .subscribe({
        next: (result) => {
          this.result.set(result);
          this.loading.set(false);
        },
        error: (error: HttpErrorResponse) => {
          this.loadError.set(problemMessage(error, 'Could not load transactions.'));
          this.loading.set(false);
        },
      });
  }

  protected setSource(value: string): void {
    this.source.set(value);
    this.firstPage();
  }

  protected setStatus(value: string): void {
    this.status.set(value as TransactionStatus | '');
    this.firstPage();
  }

  protected setPageSize(value: string): void {
    this.pageSize.set(Number(value));
    this.firstPage();
  }

  protected goTo(page: number): void {
    this.page.set(page);
    this.load();
  }

  protected runImport(): void {
    this.importing.set(true);
    this.importOutcome.set(null);
    this.api.runImport().subscribe({
      next: (summary) => {
        this.importOutcome.set({ kind: 'success', summary });
        this.importing.set(false);
        this.load();
      },
      error: (error: HttpErrorResponse) => {
        this.importOutcome.set(
          error.status === 409
            ? { kind: 'conflict', message: problemMessage(error, 'An import is already running.') }
            : { kind: 'error', message: problemMessage(error, 'The import failed.') },
        );
        this.importing.set(false);
        this.load();
      },
    });
  }

  protected select(transaction: Transaction): void {
    this.auditRequest?.unsubscribe();
    this.selected.set(transaction);
    this.audit.set(null);
    this.auditError.set(null);
    this.auditLoading.set(true);
    this.auditRequest = this.api.audit(transaction.id).subscribe({
      next: (entries) => {
        this.audit.set(entries);
        this.auditLoading.set(false);
      },
      error: (error: HttpErrorResponse) => {
        this.auditError.set(problemMessage(error, 'Could not load the audit history.'));
        this.auditLoading.set(false);
      },
    });
  }

  protected closeAudit(): void {
    this.auditRequest?.unsubscribe();
    this.selected.set(null);
    this.audit.set(null);
    this.auditLoading.set(false);
  }

  private firstPage(): void {
    this.page.set(1);
    this.load();
  }
}

/** Uses the server's ProblemDetails title/detail when present. */
function problemMessage(error: HttpErrorResponse, fallback: string): string {
  const problem = error.error as { title?: string; detail?: string } | null;
  if (problem && typeof problem === 'object' && problem.title) {
    return problem.detail ? `${problem.title} ${problem.detail}` : problem.title;
  }
  return fallback;
}
