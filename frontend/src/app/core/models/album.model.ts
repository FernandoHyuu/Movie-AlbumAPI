/**
 * Album DTOs mirroring the backend. Cover bytes are never inlined here;
 * `hasCover` is a flag and the bytes come from `GET /api/albums/{id}/cover`.
 */

/** Payload for creating or updating an Album. */
export interface AlbumWriteDto {
  title: string;
  band: string | null;
  releaseYear: number;
  genre: string | null;
}

/** Album representation returned by read and list endpoints. */
export interface AlbumDto {
  id: string;
  title: string;
  band: string | null;
  releaseYear: number;
  genre: string | null;
  hasCover: boolean;
}
