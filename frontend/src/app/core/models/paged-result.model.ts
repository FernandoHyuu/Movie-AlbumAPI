/**
 * Paginated envelope returned by the list endpoints, mirroring the backend
 * `PagedResult<T>`. `totalPages` equals ceil(`totalCount` / `pageSize`).
 */
export interface PagedResult<T> {
  /** The page of records. */
  items: T[];
  /** The 1-based page index this envelope represents. */
  pageNumber: number;
  /** The requested page size (bounded by the per-resource cap). */
  pageSize: number;
  /** Total matching records across all pages. */
  totalCount: number;
  /** Total number of pages (= ceil(totalCount / pageSize)). */
  totalPages: number;
}
