import { provideHttpClient } from '@angular/common/http';
import {
  HttpTestingController,
  provideHttpClientTesting,
} from '@angular/common/http/testing';
import { ComponentFixture, TestBed } from '@angular/core/testing';

import {
  MediaCardComponent,
  MediaCardViewModel,
} from './media-card.component';

/**
 * Example tests for the authenticated cover fetch (R8.4, R17.2).
 *
 * The cover endpoint is protected by the Movie/Album authorization policy, so
 * a bare `<img src>` request (which the browser issues without the Bearer
 * token) is rejected with 401 and every card shows the placeholder. The card
 * therefore fetches the cover through HttpClient — running the auth
 * interceptor — and binds the resulting object URL to the `<img>`.
 */
describe('MediaCardComponent (authenticated cover fetch, R17.2)', () => {
  let fixture: ComponentFixture<MediaCardComponent>;
  let component: MediaCardComponent;
  let httpMock: HttpTestingController;

  const COVER_URL = '/api/movies/abc/cover';

  const WITH_COVER: MediaCardViewModel = {
    id: 'abc',
    title: 'Blade Runner 2049',
    subtitle: 'Warner Bros.',
    coverUrl: COVER_URL,
  };

  const WITHOUT_COVER: MediaCardViewModel = {
    id: 'xyz',
    title: 'No Cover Movie',
    subtitle: null,
    coverUrl: null,
  };

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [MediaCardComponent],
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });

    fixture = TestBed.createComponent(MediaCardComponent);
    component = fixture.componentInstance;
    httpMock = TestBed.inject(HttpTestingController);
  });

  afterEach(() => {
    httpMock.verify();
    fixture.destroy();
  });

  it('fetches the cover via HttpClient and exposes an object URL', () => {
    const createdUrl = 'blob:object-url-1';
    const createSpy = spyOn(URL, 'createObjectURL').and.returnValue(createdUrl);

    fixture.componentRef.setInput('card', WITH_COVER);
    fixture.detectChanges();

    const req = httpMock.expectOne(COVER_URL);
    expect(req.request.method).toBe('GET');
    expect(req.request.responseType).toBe('blob');

    req.flush(new Blob(['img-bytes'], { type: 'image/png' }));
    fixture.detectChanges();

    expect(createSpy).toHaveBeenCalled();
    expect(component.objectUrl()).toBe(createdUrl);

    const img: HTMLImageElement | null =
      fixture.nativeElement.querySelector('.media-card__image');
    expect(img).not.toBeNull();
    expect(img!.getAttribute('src')).toBe(createdUrl);
  });

  it('shows the placeholder and issues no request when coverUrl is null', () => {
    fixture.componentRef.setInput('card', WITHOUT_COVER);
    fixture.detectChanges();

    httpMock.expectNone(() => true);
    expect(component.objectUrl()).toBeNull();

    const placeholder = fixture.nativeElement.querySelector(
      '.media-card__placeholder',
    );
    expect(placeholder).not.toBeNull();
  });

  it('falls back to the placeholder when the cover fetch fails', () => {
    fixture.componentRef.setInput('card', WITH_COVER);
    fixture.detectChanges();

    const req = httpMock.expectOne(COVER_URL);
    req.flush(new Blob(['unauthorized']), {
      status: 401,
      statusText: 'Unauthorized',
    });
    fixture.detectChanges();

    expect(component.objectUrl()).toBeNull();
    const placeholder = fixture.nativeElement.querySelector(
      '.media-card__placeholder',
    );
    expect(placeholder).not.toBeNull();
  });
});
