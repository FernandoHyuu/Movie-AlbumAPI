import {
  ACCEPTED_COVER_TYPES,
  COVER_REQUIREMENTS_MESSAGE,
  MAX_COVER_BYTES,
  buildCoverFormData,
  validateCoverFile,
} from './image-upload';

/**
 * Build a stub {@link File} of the requested size and MIME type without
 * allocating the real byte buffer for the large cases: `File.size` reflects the
 * backing Blob's byte length, so a single sentinel byte is enough for the
 * accepted cases while the oversized case overrides `size` directly to avoid
 * allocating a multi-megabyte buffer.
 */
function makeFile(type: string, size: number, name = 'cover'): File {
  const file = new File([new Uint8Array(Math.min(size, 1))], name, { type });
  if (size !== file.size) {
    Object.defineProperty(file, 'size', { value: size });
  }
  return file;
}

describe('validateCoverFile (R20.6)', () => {
  it('accepts a JPEG under the 5 MB cap', () => {
    const result = validateCoverFile(makeFile('image/jpeg', 1024, 'poster.jpg'));

    expect(result.valid).toBe(true);
    expect(result.message).toBeUndefined();
  });

  it('accepts a PNG under the 5 MB cap', () => {
    const result = validateCoverFile(makeFile('image/png', MAX_COVER_BYTES - 1, 'poster.png'));

    expect(result.valid).toBe(true);
  });

  it('accepts a JPEG exactly at the 5 MB cap', () => {
    const result = validateCoverFile(makeFile('image/jpeg', MAX_COVER_BYTES, 'edge.jpg'));

    expect(result.valid).toBe(true);
  });

  it('rejects a file larger than 5 MB and names the allowed size/formats', () => {
    const result = validateCoverFile(makeFile('image/jpeg', MAX_COVER_BYTES + 1, 'huge.jpg'));

    expect(result.valid).toBe(false);
    expect(result.message).toBe(COVER_REQUIREMENTS_MESSAGE);
  });

  it('rejects a GIF (non-JPEG/PNG format)', () => {
    const result = validateCoverFile(makeFile('image/gif', 1024, 'anim.gif'));

    expect(result.valid).toBe(false);
    expect(result.message).toBe(COVER_REQUIREMENTS_MESSAGE);
  });

  it('rejects a PDF (non-image format)', () => {
    const result = validateCoverFile(makeFile('application/pdf', 1024, 'doc.pdf'));

    expect(result.valid).toBe(false);
    expect(result.message).toBe(COVER_REQUIREMENTS_MESSAGE);
  });

  it('rejects an oversized file regardless of an accepted type', () => {
    expect(ACCEPTED_COVER_TYPES).toContain('image/png');
    const result = validateCoverFile(makeFile('image/png', MAX_COVER_BYTES + 10, 'big.png'));

    expect(result.valid).toBe(false);
  });
});

describe('buildCoverFormData (R20.5)', () => {
  it('appends the file under the "file" field preserving its name', () => {
    const file = makeFile('image/jpeg', 2048, 'cover.jpg');

    const form = buildCoverFormData(file);
    const sent = form.get('file');

    expect(sent instanceof File).toBe(true);
    expect((sent as File).name).toBe('cover.jpg');
    expect((sent as File).type).toBe('image/jpeg');
  });
});
