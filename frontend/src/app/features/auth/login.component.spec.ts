import { ComponentFixture, TestBed } from '@angular/core/testing';
import { ActivatedRoute, Router, convertToParamMap } from '@angular/router';
import { HttpErrorResponse } from '@angular/common/http';
import { Observable, of, throwError } from 'rxjs';

import { AuthService } from '../../core/services/auth.service';
import { NotificationService } from '../../core/services/notification.service';
import { AuthResponse, LoginRequest } from '../../core/models/auth.model';
import { DEFAULT_POST_LOGIN_ROUTE, LoginComponent } from './login.component';

/** A stubbed {@link AuthService.login} the tests program per scenario. */
class AuthServiceStub {
  response: Observable<AuthResponse> = of(makeAuthResponse());
  lastRequest: LoginRequest | null = null;

  login(request: LoginRequest): Observable<AuthResponse> {
    this.lastRequest = request;
    return this.response;
  }
}

/** Captures the error payload passed to {@link NotificationService.error}. */
class NotificationServiceStub {
  errorCalls: unknown[] = [];
  error(problem: unknown): number {
    this.errorCalls.push(problem);
    return 0;
  }
}

function makeAuthResponse(): AuthResponse {
  return {
    accessToken: 'acc',
    refreshToken: 'ref',
    role: 'Admin',
    accessTokenExpiresAt: '2025-01-01T00:15:00Z',
  };
}

describe('LoginComponent', () => {
  let auth: AuthServiceStub;
  let notifications: NotificationServiceStub;
  let router: Router;
  let returnUrl: string | null;

  function setup(returnUrlParam: string | null = null): ComponentFixture<LoginComponent> {
    returnUrl = returnUrlParam;
    auth = new AuthServiceStub();
    notifications = new NotificationServiceStub();

    TestBed.configureTestingModule({
      imports: [LoginComponent],
      providers: [
        { provide: AuthService, useValue: auth },
        { provide: NotificationService, useValue: notifications },
        {
          provide: ActivatedRoute,
          useValue: {
            snapshot: {
              queryParamMap: convertToParamMap(
                returnUrl === null ? {} : { returnUrl },
              ),
            },
          },
        },
      ],
    });

    router = TestBed.inject(Router);
    spyOn(router, 'navigateByUrl').and.resolveTo(true);

    const fixture = TestBed.createComponent(LoginComponent);
    fixture.detectChanges();
    return fixture;
  }

  function fillForm(fixture: ComponentFixture<LoginComponent>): void {
    fixture.componentInstance.form.setValue({
      email: 'user@example.com',
      password: 'password123',
    });
  }

  it('does not submit while the form is invalid', () => {
    const fixture = setup();
    const spy = spyOn(auth, 'login').and.callThrough();

    fixture.componentInstance.submit();

    expect(spy).not.toHaveBeenCalled();
    expect(router.navigateByUrl).not.toHaveBeenCalled();
  });

  it('submits credentials and navigates to the returnUrl on success (R15.1)', () => {
    const fixture = setup('/albums/42');
    fillForm(fixture);

    fixture.componentInstance.submit();

    expect(auth.lastRequest).toEqual({
      email: 'user@example.com',
      password: 'password123',
    });
    expect(router.navigateByUrl).toHaveBeenCalledWith('/albums/42');
    expect(fixture.componentInstance.pending()).toBe(false);
  });

  it('navigates to the default route when no returnUrl is present (R15.1)', () => {
    const fixture = setup(null);
    fillForm(fixture);

    fixture.componentInstance.submit();

    expect(router.navigateByUrl).toHaveBeenCalledWith(DEFAULT_POST_LOGIN_ROUTE);
  });

  it('navigates to the default route when returnUrl is empty (R15.1)', () => {
    const fixture = setup('');
    fillForm(fixture);

    fixture.componentInstance.submit();

    expect(router.navigateByUrl).toHaveBeenCalledWith(DEFAULT_POST_LOGIN_ROUTE);
  });

  it('shows an error notification and does not navigate on failure', () => {
    const fixture = setup();
    fillForm(fixture);
    auth.response = throwError(
      () =>
        new HttpErrorResponse({
          status: 401,
          error: { detail: 'Invalid credentials' },
        }),
    );

    fixture.componentInstance.submit();

    expect(notifications.errorCalls.length).toBe(1);
    expect(notifications.errorCalls[0]).toEqual({ detail: 'Invalid credentials' });
    expect(router.navigateByUrl).not.toHaveBeenCalled();
    // Form stays editable for a retry.
    expect(fixture.componentInstance.pending()).toBe(false);
  });
});
