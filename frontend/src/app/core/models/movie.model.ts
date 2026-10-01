/**
 * Movie DTOs mirroring the backend. Cover bytes are never inlined here;
 * `hasCover` is a flag and the bytes come from `GET /api/movies/{id}/cover`.
 */

/** Payload for creating or updating a Movie. */
export interface MovieWriteDto {
  title: string;
  studio: string | null;
  releaseYear: number;
  mainActors: string[];
}

/** Movie representation returned by read and list endpoints. */
export interface MovieDto {
  id: string;
  title: string;
  studio: string | null;
  releaseYear: number;
  mainActors: string[];
  hasCover: boolean;
}
