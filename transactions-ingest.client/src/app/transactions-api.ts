import { HttpClient, HttpParams } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { Observable } from 'rxjs';

export type TransactionStatus = 'Active' | 'Revoked' | 'Finalized';

export const STATUSES: readonly TransactionStatus[] = ['Active', 'Revoked', 'Finalized'];

/** The ticket's three sources; the API has no endpoint listing them. */
export const SOURCE_SYSTEMS: readonly string[] = ['Store Uptown', 'Store Downtown', 'Online'];

export interface Transaction {
  id: number;
  sourceSystem: string;
  transactionId: number;
  locationCode: string;
  productName: string;
  amount: number;
  /** ISO 8601 UTC, e.g. 2026-02-27T08:15:30Z. */
  transactionTime: string;
  status: TransactionStatus;
}

export interface PagedResult<T> {
  items: T[];
  page: number;
  pageSize: number;
  totalCount: number;
}

export interface TransactionQuery {
  page: number;
  pageSize: number;
  /** Empty means all sources. */
  source: string;
  /** Empty means all statuses. */
  status: TransactionStatus | '';
}

export interface AuditEntry {
  changedAt: string;
  changeType: 'Created' | 'Updated' | 'Revoked' | 'Reinstated' | 'Finalized';
  fieldName: string | null;
  oldValue: string | null;
  newValue: string | null;
  importRunId: number;
}

export interface SourceSummary {
  sourceSystem: string;
  status: 'Succeeded' | 'Failed';
  error: string | null;
  fetched: number;
  inserted: number;
  updated: number;
  revoked: number;
  reinstated: number;
  rejected: number;
  ignored: number;
  revocationSkipped: boolean;
}

export interface ImportRunSummary {
  runId: number;
  status: 'Running' | 'Succeeded' | 'PartiallyFailed' | 'Failed';
  startedAt: string;
  completedAt: string;
  finalized: number;
  sources: SourceSummary[];
}

/** Thin wrapper over the server API. Paging and filtering happen on the server. */
@Injectable({ providedIn: 'root' })
export class TransactionsApi {
  private readonly http = inject(HttpClient);

  list(query: TransactionQuery): Observable<PagedResult<Transaction>> {
    let params = new HttpParams().set('page', query.page).set('pageSize', query.pageSize);
    if (query.source) {
      params = params.set('source', query.source);
    }
    if (query.status) {
      params = params.set('status', query.status);
    }
    return this.http.get<PagedResult<Transaction>>('/api/transactions', { params });
  }

  audit(id: number): Observable<AuditEntry[]> {
    return this.http.get<AuditEntry[]>(`/api/transactions/${id}/audit`);
  }

  runImport(): Observable<ImportRunSummary> {
    return this.http.post<ImportRunSummary>('/api/import', null);
  }
}
