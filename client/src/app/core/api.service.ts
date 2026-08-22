import { HttpClient, HttpErrorResponse } from '@angular/common/http';
import { Injectable, inject } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { RUNTIME_CONFIG } from './runtime-config';
import type {
  ComponentResponse,
  CreateComponentRequest,
  DeclareIncidentRequest,
  IncidentResponse,
  PostIncidentUpdateRequest,
  ProblemDetails,
  UpdateComponentRequest,
} from './api.models';

/** A failure the console can put in front of an operator. */
export class ApiError extends Error {
  constructor(
    message: string,
    readonly status: number,
    readonly fieldErrors: Readonly<Record<string, readonly string[]>> = {},
  ) {
    super(message);
    this.name = 'ApiError';
  }
}

/**
 * The operator console's calls to the API.
 *
 * Every failure is turned into an ApiError carrying what the server actually said. The API
 * answers in RFC 9457, and the whole reason it goes to the trouble is so a client can tell an
 * error from a payload and show the operator the detail rather than "something went wrong".
 */
@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  private readonly config = inject(RUNTIME_CONFIG);

  listComponents(): Promise<ComponentResponse[]> {
    return this.send(() =>
      firstValueFrom(this.http.get<ComponentResponse[]>(`${this.base}/components`)),
    );
  }

  createComponent(request: CreateComponentRequest): Promise<ComponentResponse> {
    return this.send(() =>
      firstValueFrom(this.http.post<ComponentResponse>(`${this.base}/components`, request)),
    );
  }

  updateComponent(id: string, request: UpdateComponentRequest): Promise<ComponentResponse> {
    return this.send(() =>
      firstValueFrom(this.http.put<ComponentResponse>(`${this.base}/components/${id}`, request)),
    );
  }

  deleteComponent(id: string): Promise<void> {
    return this.send(async () => {
      await firstValueFrom(this.http.delete(`${this.base}/components/${id}`));
    });
  }

  listIncidents(): Promise<IncidentResponse[]> {
    return this.send(() =>
      firstValueFrom(this.http.get<IncidentResponse[]>(`${this.base}/incidents`)),
    );
  }

  declareIncident(request: DeclareIncidentRequest): Promise<IncidentResponse> {
    return this.send(() =>
      firstValueFrom(this.http.post<IncidentResponse>(`${this.base}/incidents`, request)),
    );
  }

  postIncidentUpdate(id: string, request: PostIncidentUpdateRequest): Promise<IncidentResponse> {
    return this.send(() =>
      firstValueFrom(
        this.http.post<IncidentResponse>(`${this.base}/incidents/${id}/updates`, request),
      ),
    );
  }

  rebuildReadModel(): Promise<void> {
    return this.send(async () => {
      await firstValueFrom(this.http.post(`${this.base}/read-model/rebuild`, null));
    });
  }

  private get base(): string {
    return this.config.apiUrl;
  }

  private async send<T>(call: () => Promise<T>): Promise<T> {
    try {
      return await call();
    } catch (error) {
      throw ApiService.toApiError(error);
    }
  }

  private static toApiError(error: unknown): ApiError {
    if (!(error instanceof HttpErrorResponse)) {
      return new ApiError('Something went wrong.', 0);
    }

    if (error.status === 0) {
      return new ApiError('The API could not be reached.', 0);
    }

    const problem = error.error as ProblemDetails | null;

    // The detail is the sentence written for a caller; the title is the category. Preferring
    // the detail is how "A component with the slug 'api' already exists" reaches an operator
    // instead of "Conflict".
    const message =
      problem?.detail ??
      problem?.title ??
      (error.status === 401 ? 'Those credentials were not accepted.' : 'The request was refused.');

    return new ApiError(message, error.status, problem?.errors ?? {});
  }
}
