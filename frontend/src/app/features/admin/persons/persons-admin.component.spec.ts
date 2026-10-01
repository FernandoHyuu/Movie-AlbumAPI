import { HttpErrorResponse } from '@angular/common/http';
import { ComponentFixture, TestBed } from '@angular/core/testing';
import { Observable, of, throwError } from 'rxjs';

import { PersonDto, PersonWriteDto } from '../../../core/models/person.model';
import { Role } from '../../../core/models/role.enum';
import { NotificationService } from '../../../core/services/notification.service';
import { PagedResult } from '../shared/paged-result.model';
import { PersonAdminService } from '../shared/person-admin.service';
import { PersonsAdminComponent } from './persons-admin.component';

function makePerson(overrides: Partial<PersonDto> = {}): PersonDto {
  return {
    id: 'p1',
    name: 'Ana Admin',
    email: 'ana@example.com',
    role: Role.Admin,
    createdAt: '2025-01-01T00:00:00Z',
    addresses: [],
    phones: [],
    ...overrides,
  };
}

function page(items: PersonDto[], totalPages = 1): PagedResult<PersonDto> {
  return { items, pageNumber: 1, pageSize: 50, totalCount: items.length, totalPages };
}

/** Stub of {@link PersonAdminService} programmed per scenario. */
class PersonServiceStub {
  listCalls = 0;
  listResult: Observable<PagedResult<PersonDto>> = of(page([makePerson()]));

  createArg: PersonWriteDto | null = null;
  createResult: Observable<PersonDto> = of(makePerson({ id: 'created' }));

  updateArg: { id: string; dto: PersonWriteDto } | null = null;
  updateResult: Observable<PersonDto> = of(makePerson());

  deleteArg: string | null = null;
  deleteResult: Observable<void> = of(undefined);

  list(): Observable<PagedResult<PersonDto>> {
    this.listCalls++;
    return this.listResult;
  }
  create(dto: PersonWriteDto): Observable<PersonDto> {
    this.createArg = dto;
    return this.createResult;
  }
  update(id: string, dto: PersonWriteDto): Observable<PersonDto> {
    this.updateArg = { id, dto };
    return this.updateResult;
  }
  delete(id: string): Observable<void> {
    this.deleteArg = id;
    return this.deleteResult;
  }
}

class NotificationStub {
  successCalls: string[] = [];
  errorCalls: unknown[] = [];
  success(message: string): number {
    this.successCalls.push(message);
    return 0;
  }
  error(problem: unknown): number {
    this.errorCalls.push(problem);
    return 0;
  }
}

describe('PersonsAdminComponent', () => {
  let service: PersonServiceStub;
  let notifications: NotificationStub;

  function setup(): ComponentFixture<PersonsAdminComponent> {
    service = new PersonServiceStub();
    notifications = new NotificationStub();

    TestBed.configureTestingModule({
      imports: [PersonsAdminComponent],
      providers: [
        { provide: PersonAdminService, useValue: service },
        { provide: NotificationService, useValue: notifications },
      ],
    });

    const fixture = TestBed.createComponent(PersonsAdminComponent);
    fixture.detectChanges(); // triggers constructor load()
    return fixture;
  }

  function fillValidCreate(fixture: ComponentFixture<PersonsAdminComponent>): void {
    fixture.componentInstance.form.patchValue({
      name: 'New Person',
      email: 'new@example.com',
      role: Role.User_Movie,
      password: 'password123',
    });
  }

  it('loads the first page on init (R20.1)', () => {
    const fixture = setup();

    expect(service.listCalls).toBe(1);
    expect(fixture.componentInstance.items().length).toBe(1);
  });

  it('creates a person, shows a success toast and refreshes the table (R20.3)', () => {
    const fixture = setup();
    fillValidCreate(fixture);

    fixture.componentInstance.submit();

    expect(service.createArg?.name).toBe('New Person');
    expect(service.createArg?.email).toBe('new@example.com');
    expect(service.createArg?.password).toBe('password123');
    expect(notifications.successCalls).toEqual(['Person created.']);
    expect(service.listCalls).toBe(2);
  });

  it('updates a person without requiring a password and shows a success toast (R20.3)', () => {
    const fixture = setup();
    fixture.componentInstance.edit(makePerson({ id: 'p1', name: 'Old Name' }));
    fixture.componentInstance.form.patchValue({ name: 'Edited Name' });

    fixture.componentInstance.submit();

    expect(service.updateArg?.id).toBe('p1');
    expect(service.updateArg?.dto.name).toBe('Edited Name');
    // Blank password on edit leaves it unchanged (sent as null).
    expect(service.updateArg?.dto.password).toBeNull();
    expect(notifications.successCalls).toEqual(['Person updated.']);
    expect(service.listCalls).toBe(2);
  });

  it('deletes a person and reloads the current page on success (R20.7)', () => {
    const fixture = setup();

    fixture.componentInstance.remove(makePerson({ id: 'p1' }));

    expect(service.deleteArg).toBe('p1');
    expect(notifications.successCalls).toEqual(['Person deleted.']);
    expect(service.listCalls).toBe(2);
  });

  it('preserves the table and retains the form when create fails (R20.8, R20.4)', () => {
    const fixture = setup();
    fillValidCreate(fixture);
    service.createResult = throwError(
      () => new HttpErrorResponse({ status: 409, error: { detail: 'Email exists' } }),
    );

    fixture.componentInstance.submit();

    expect(notifications.errorCalls.length).toBe(1);
    expect(notifications.errorCalls[0]).toEqual({ detail: 'Email exists' });
    expect(service.listCalls).toBe(1); // no reload
    expect(fixture.componentInstance.items().length).toBe(1);
    // Form data retained for correction.
    expect(fixture.componentInstance.form.getRawValue().name).toBe('New Person');
    expect(fixture.componentInstance.form.getRawValue().email).toBe('new@example.com');
  });

  it('preserves the table and shows an error toast when delete fails (R20.8)', () => {
    const fixture = setup();
    service.deleteResult = throwError(
      () => new HttpErrorResponse({ status: 500, error: { detail: 'nope' } }),
    );

    fixture.componentInstance.remove(makePerson({ id: 'p1' }));

    expect(notifications.errorCalls.length).toBe(1);
    expect(notifications.successCalls.length).toBe(0);
    expect(service.listCalls).toBe(1);
    expect(fixture.componentInstance.items().length).toBe(1);
  });

  it('does not submit a form with a missing name and flags the field (R20.4)', () => {
    const fixture = setup();
    fixture.componentInstance.form.patchValue({
      name: '',
      email: 'ok@example.com',
      role: Role.User_Movie,
      password: 'password123',
    });

    fixture.componentInstance.submit();

    expect(service.createArg).toBeNull();
    expect(service.listCalls).toBe(1);
    expect(fixture.componentInstance.form.controls.name.invalid).toBe(true);
    // Other entered values retained.
    expect(fixture.componentInstance.form.getRawValue().email).toBe('ok@example.com');
  });

  it('does not submit a form with a malformed email and retains the entered data (R20.4)', () => {
    const fixture = setup();
    fixture.componentInstance.form.patchValue({
      name: 'Someone',
      email: 'not-an-email',
      role: Role.User_Movie,
      password: 'password123',
    });

    fixture.componentInstance.submit();

    expect(service.createArg).toBeNull();
    expect(service.listCalls).toBe(1);
    expect(fixture.componentInstance.form.controls.email.invalid).toBe(true);
    // Entered (invalid) value retained so the admin can correct it.
    expect(fixture.componentInstance.form.getRawValue().email).toBe('not-an-email');
    expect(fixture.componentInstance.form.getRawValue().name).toBe('Someone');
  });

  it('requires a password when creating a new person (R20.4)', () => {
    const fixture = setup();
    fixture.componentInstance.form.patchValue({
      name: 'Someone',
      email: 'ok@example.com',
      role: Role.User_Movie,
      password: '',
    });

    fixture.componentInstance.submit();

    expect(service.createArg).toBeNull();
    expect(fixture.componentInstance.form.controls.password.invalid).toBe(true);
  });
});
