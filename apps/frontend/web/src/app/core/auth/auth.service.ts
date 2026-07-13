import { computed, Injectable, inject, signal } from '@angular/core';
import { RolesEnum } from '@hsm/common/enums';
import { catchError, map, type Observable, of, switchMap, tap } from 'rxjs';

import { ApiClient } from '../api/api-client';
import type {
  LoginPayload,
  OnboardingPayload,
  SignupPayload,
  Tokens,
  UserProfile,
} from '../api/response';
import { CsrfService } from './csrf.service';

/** Backend auth endpoints (relative to the `/v1` base URL). */
export const AUTH_LOGIN_PATH = '/auth/login';
export const AUTH_SIGNUP_PATH = '/auth/signup';
export const AUTH_REFRESH_PATH = '/auth/refresh';
export const AUTH_PROFILE_PATH = '/auth/profile';
export const AUTH_LOGOUT_PATH = '/auth/logout';
export const AUTH_ONBOARDING_PATH = '/auth/onboarding';

/**
 * The role values that, on their own, mark a user as a Patient (not staff).
 * A user whose roles are entirely within this set is a patient; any role
 * outside it makes them staff. Sourced from `RolesEnum.Patient` so the two
 * sides never drift.
 */
const PATIENT_ROLES: ReadonlySet<string> = new Set([
  RolesEnum.Patient.Patient,
  RolesEnum.Patient.Family,
]);

/**
 * Holds authentication state and orchestrates login / logout / profile load.
 *
 * State is modelled with signals:
 * - {@link currentUser} — the signed-in profile, or `null` when anonymous.
 * - {@link isAuthenticated} — `computed` from `currentUser`.
 * - {@link isAdmin} — `computed`; true iff the profile's roles include
 *   `RolesEnum.System.Admin` (the string `'admin'`).
 *
 * The refresh interceptor (`auth.interceptor.ts`) performs the transparent
 * cookie refresh; on a dead refresh cookie it calls {@link onSessionLost} to
 * clear state. The session lives in httpOnly cookies — no client-side storage.
 */
@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly api = inject(ApiClient);
  private readonly csrf = inject(CsrfService);

  private readonly currentUserSignal = signal<UserProfile | null>(null);

  /** The signed-in user profile, or `null` when not authenticated. */
  readonly currentUser = this.currentUserSignal.asReadonly();

  /** True when a user profile is loaded. */
  readonly isAuthenticated = computed(() => this.currentUserSignal() !== null);

  /** True iff the current user's roles include `RolesEnum.System.Admin`. */
  readonly isAdmin = computed(() => this.hasRole(RolesEnum.System.Admin));

  /**
   * True iff a profile is loaded AND it carries at least one role OUTSIDE the
   * patient set ({@link RolesEnum.Patient.Patient} / `.Family`) — i.e. any
   * clinical, administrative, system (incl. admin), etc. role. Anonymous users
   * are false (no profile). This is the "front door" discriminator: staff land
   * in `/workspace`, see the staff nav, and are admin-created.
   */
  readonly isStaff = computed(() => {
    const user = this.currentUserSignal();
    if (!user) {
      return false;
    }
    return user.roles.some(role => !PATIENT_ROLES.has(role));
  });

  /**
   * True iff a profile is loaded AND every role is within the patient set
   * ({@link RolesEnum.Patient.Patient} / `.Family`) — i.e. it is non-null and
   * NOT staff. Anonymous users are false. Patients self-register and land in
   * `/patient`.
   */
  readonly isPatient = computed(
    () => this.currentUserSignal() !== null && !this.isStaff(),
  );

  /**
   * True iff a profile is loaded AND it is still pending first-login onboarding
   * (`onboardingCompletedAt == null`). Anonymous users are false (no profile);
   * patients and the seeded admin are created complete, so they are false too.
   * The pending-onboarding guard reads this to force staff to `/onboarding`.
   */
  readonly needsOnboarding = computed(() => {
    const user = this.currentUserSignal();
    return user !== null && user.onboardingCompletedAt == null;
  });

  /** True iff the current user's roles include the given role value. */
  hasRole(role: string): boolean {
    return this.currentUserSignal()?.roles?.includes(role) ?? false;
  }

  /** True iff the current user holds at least one of the given roles. */
  hasAnyRole(roles: readonly string[]): boolean {
    const userRoles = this.currentUserSignal()?.roles;
    if (!userRoles) {
      return false;
    }
    return roles.some(role => userRoles.includes(role));
  }

  /**
   * Authenticates with username + password. The response sets the httpOnly
   * session cookies (no token is persisted client-side); then loads the
   * profile. Emits the profile on success; errors with the `ApiError` thrown by
   * {@link ApiClient} (e.g. invalid credentials).
   */
  login(payload: LoginPayload): Observable<UserProfile> {
    return this.api
      .post<Tokens>(AUTH_LOGIN_PATH, payload)
      .pipe(switchMap(() => this.loadProfile()));
  }

  /**
   * Self-registers via `POST /v1/auth/signup` (the backend rejects privileged
   * roles). The response sets the session cookies; loads the profile — same
   * shape as {@link login}, so the new user lands authenticated. Errors with
   * the `ApiError` thrown by {@link ApiClient} (e.g. a duplicate username).
   */
  register(payload: SignupPayload): Observable<UserProfile> {
    return this.api
      .post<Tokens>(AUTH_SIGNUP_PATH, payload)
      .pipe(switchMap(() => this.loadProfile()));
  }

  /**
   * Completes first-login onboarding for a pending staff member via
   * `POST /v1/auth/onboarding` (sets a new password + required contact info).
   * The backend reissues the session cookies so the cleared pending flag is
   * reflected: reload the profile — mirroring {@link login}. After this emits,
   * {@link needsOnboarding} is false (the
   * reloaded profile carries a non-null `onboardingCompletedAt`). Errors with
   * the `ApiError` thrown by {@link ApiClient} (e.g. a 400 for a confirm-email
   * mismatch or a too-short password).
   */
  completeOnboarding(payload: OnboardingPayload): Observable<UserProfile> {
    return this.api
      .post<Tokens>(AUTH_ONBOARDING_PATH, payload)
      .pipe(switchMap(() => this.loadProfile()));
  }

  /**
   * Fetches `GET /v1/auth/profile` (session cookie rides automatically) and
   * stores it as the current user. Used after login and on app init.
   */
  loadProfile(): Observable<UserProfile> {
    return this.api
      .get<UserProfile>(AUTH_PROFILE_PATH)
      .pipe(tap(profile => this.currentUserSignal.set(profile)));
  }

  /**
   * Restores the session on app start by probing `GET /v1/auth/profile`. The
   * session cookie rides automatically; the interceptor transparently refreshes
   * on an expired access cookie. A failure (no/dead session) clears state and
   * resolves to `null` — anonymous, no token to inspect first.
   */
  restoreSession(): Observable<UserProfile | null> {
    return this.loadProfile().pipe(
      catchError(() => {
        this.onSessionLost();
        return of(null);
      }),
    );
  }

  /**
   * Logs out: best-effort calls `GET /v1/auth/logout`, then clears tokens and
   * resets auth state regardless of the call's outcome.
   */
  logout(): Observable<void> {
    return this.api.get<void>(AUTH_LOGOUT_PATH).pipe(
      catchError(() => of(undefined)),
      tap(() => this.onSessionLost()),
      map(() => undefined),
    );
  }

  /** Clears cached CSRF token and resets auth state. Called on logout / dead
   * refresh (the server clears the httpOnly cookies). */
  onSessionLost(): void {
    this.csrf.clear();
    this.currentUserSignal.set(null);
  }
}
