/**
 * Person models mirroring the backend Person DTOs. The password hash is never
 * exposed; a plaintext password is sent only on write and is optional on update.
 */

/** An address value within a Person payload. */
export interface AddressDto {
  street: string;
  city: string;
  state: string;
  zipCode: string;
}

/** A phone value within a Person payload. */
export interface PhoneDto {
  number: string;
  type: string;
}

/**
 * Payload for creating or updating a Person. `role` is the string name of a
 * {@link Role}. A Person may carry 0–10 addresses and 0–10 phones; `password`
 * is optional on update.
 */
export interface PersonWriteDto {
  name: string;
  email: string;
  role: string;
  password: string | null;
  addresses: AddressDto[];
  phones: PhoneDto[];
}

/** Person representation returned by read and list endpoints. */
export interface PersonDto {
  id: string;
  name: string;
  email: string;
  role: string;
  createdAt: string;
  addresses: AddressDto[];
  phones: PhoneDto[];
}
