// Re-exports the canonical PagedResult from core so the definition lives in one
// place; the admin-only page size stays here.
export type { PagedResult } from '../../../core/models/paged-result.model';

/** Maximum records a management table requests per page. */
export const ADMIN_PAGE_SIZE = 50;
