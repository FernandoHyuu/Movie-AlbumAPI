import {
  ChangeDetectionStrategy,
  Component,
  WritableSignal,
  computed,
  inject,
  signal,
} from '@angular/core';
import {
  FormArray,
  FormBuilder,
  FormGroup,
  ReactiveFormsModule,
  Validators,
} from '@angular/forms';

import { PersonDto, PersonWriteDto } from '../../../core/models/person.model';
import { Role } from '../../../core/models/role.enum';
import { NotificationService } from '../../../core/services/notification.service';
import { toProblemDetails } from '../shared/http-error';
import { ADMIN_PAGE_SIZE } from '../shared/paged-result.model';
import { PersonAdminService } from '../shared/person-admin.service';

/** Selectable roles for the Person form, matching the backend `Role` enum. */
const ROLE_OPTIONS: readonly Role[] = [
  Role.Admin,
  Role.User_Movie,
  Role.User_Album,
  Role.User_Full,
];

/**
 * Persons CRUD table and form. Reactive validators mirror the backend contract,
 * including 0–10 addresses and phones and a password that is required on create
 * but optional on edit. Failures keep the table and the entered form data intact.
 */
@Component({
  selector: 'app-persons-admin',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [ReactiveFormsModule],
  templateUrl: './persons-admin.component.html',
  styleUrl: '../admin.component.css',
})
export class PersonsAdminComponent {
  private readonly fb = inject(FormBuilder);
  private readonly service = inject(PersonAdminService);
  private readonly notifications = inject(NotificationService);

  readonly roleOptions = ROLE_OPTIONS;

  readonly items: WritableSignal<readonly PersonDto[]> = signal([]);

  /** 1-based current page number. */
  readonly page = signal(1);

  readonly totalPages = signal(1);

  readonly loading = signal(false);

  /** The id being edited, or `null` when creating a new Person. */
  readonly editingId: WritableSignal<string | null> = signal(null);

  /** `true` while a create/update/delete request is in flight. */
  readonly saving = signal(false);

  /** Drives the password-optional-on-edit rule. */
  readonly isEditing = computed(() => this.editingId() !== null);

  readonly form = this.fb.nonNullable.group({
    name: ['', [Validators.required, Validators.maxLength(500)]],
    email: [
      '',
      [Validators.required, Validators.email, Validators.minLength(5), Validators.maxLength(254)],
    ],
    role: [Role.User_Movie as string, [Validators.required]],
    password: ['', [Validators.minLength(8), Validators.maxLength(128)]],
    addresses: this.fb.array([] as FormGroup[]),
    phones: this.fb.array([] as FormGroup[]),
  });

  constructor() {
    this.applyPasswordRule();
    this.load();
  }

  /** Addresses sub-form array (0–10 entries). */
  get addresses(): FormArray {
    return this.form.controls.addresses as unknown as FormArray;
  }

  /** Phones sub-form array (0–10 entries). */
  get phones(): FormArray {
    return this.form.controls.phones as unknown as FormArray;
  }

  load(): void {
    this.loading.set(true);
    this.service.list(this.page(), ADMIN_PAGE_SIZE).subscribe({
      next: (result) => {
        this.items.set(result.items);
        this.totalPages.set(Math.max(1, result.totalPages));
        this.loading.set(false);
      },
      error: (error: unknown) => {
        this.loading.set(false);
        this.notifications.error(toProblemDetails(error));
      },
    });
  }

  /** Go to a specific 1-based page, clamped to the available range. */
  goToPage(page: number): void {
    const clamped = Math.min(Math.max(1, page), this.totalPages());
    if (clamped !== this.page()) {
      this.page.set(clamped);
      this.load();
    }
  }

  /** `true` when a top-level control is invalid and has been touched or changed. */
  isInvalid(controlName: 'name' | 'email' | 'role' | 'password'): boolean {
    const control = this.form.controls[controlName];
    return control.invalid && (control.touched || control.dirty);
  }

  /** Begin editing an existing Person, populating the form (password left blank). */
  edit(person: PersonDto): void {
    this.editingId.set(person.id);
    this.addresses.clear();
    this.phones.clear();
    person.addresses.forEach((a) =>
      this.addresses.push(this.buildAddressGroup(a.street, a.city, a.state, a.zipCode)),
    );
    person.phones.forEach((p) => this.phones.push(this.buildPhoneGroup(p.number, p.type)));
    this.form.patchValue({
      name: person.name,
      email: person.email,
      role: person.role,
      password: '',
    });
    this.applyPasswordRule();
  }

  /** Reset the form back to create mode. */
  resetForm(): void {
    this.editingId.set(null);
    this.addresses.clear();
    this.phones.clear();
    this.form.reset({
      name: '',
      email: '',
      role: Role.User_Movie,
      password: '',
    });
    this.applyPasswordRule();
  }

  /** Add an empty address row (up to 10). */
  addAddress(): void {
    if (this.addresses.length < 10) {
      this.addresses.push(this.buildAddressGroup('', '', '', ''));
    }
  }

  /** Remove the address row at the given index. */
  removeAddress(index: number): void {
    this.addresses.removeAt(index);
  }

  /** Add an empty phone row (up to 10). */
  addPhone(): void {
    if (this.phones.length < 10) {
      this.phones.push(this.buildPhoneGroup('', ''));
    }
  }

  /** Remove the phone row at the given index. */
  removePhone(index: number): void {
    this.phones.removeAt(index);
  }

  /** Save the form. On failure the table and the entered form data are left intact. */
  submit(): void {
    if (this.form.invalid || this.saving()) {
      this.form.markAllAsTouched();
      return;
    }

    const dto = this.toDto();
    this.saving.set(true);
    const id = this.editingId();
    const request$ = id ? this.service.update(id, dto) : this.service.create(dto);

    request$.subscribe({
      next: () => {
        this.saving.set(false);
        this.notifications.success(id ? 'Person updated.' : 'Person created.');
        this.resetForm();
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.notifications.error(toProblemDetails(error));
        // Form data is left intact so the user can correct and resubmit.
      },
    });
  }

  /** Delete a Person, reloading the current page on success. */
  remove(person: PersonDto): void {
    if (this.saving()) {
      return;
    }
    this.saving.set(true);
    this.service.delete(person.id).subscribe({
      next: () => {
        this.saving.set(false);
        this.notifications.success('Person deleted.');
        if (this.editingId() === person.id) {
          this.resetForm();
        }
        this.load();
      },
      error: (error: unknown) => {
        this.saving.set(false);
        this.notifications.error(toProblemDetails(error));
      },
    });
  }

  /** Build the write DTO from the current form value. */
  private toDto(): PersonWriteDto {
    const value = this.form.getRawValue();
    const password = value.password.trim();
    return {
      name: value.name,
      email: value.email,
      role: value.role,
      password: password.length > 0 ? password : null,
      addresses: this.addresses.getRawValue(),
      phones: this.phones.getRawValue(),
    };
  }

  /**
   * Password is required on create but optional on edit, where a blank value
   * leaves the stored password unchanged. Length bounds apply when a value is set.
   */
  private applyPasswordRule(): void {
    const control = this.form.controls.password;
    const validators = [Validators.minLength(8), Validators.maxLength(128)];
    if (!this.isEditing()) {
      validators.push(Validators.required);
    }
    control.setValidators(validators);
    control.updateValueAndValidity({ emitEvent: false });
  }

  private buildAddressGroup(
    street: string,
    city: string,
    state: string,
    zipCode: string,
  ): FormGroup {
    return this.fb.nonNullable.group({
      street: [street, [Validators.required, Validators.maxLength(500)]],
      city: [city, [Validators.required, Validators.maxLength(500)]],
      state: [state, [Validators.required, Validators.maxLength(500)]],
      zipCode: [zipCode, [Validators.required, Validators.maxLength(500)]],
    });
  }

  private buildPhoneGroup(num: string, type: string): FormGroup {
    return this.fb.nonNullable.group({
      number: [num, [Validators.required, Validators.maxLength(500)]],
      type: [type, [Validators.required, Validators.maxLength(500)]],
    });
  }
}
