import { ApplicationConfig } from '@angular/core';

// The ApiService talks to /api with fetch directly (incl. SSE), so no HttpClient
// provider is needed. Kept minimal on purpose.
export const appConfig: ApplicationConfig = {
  providers: []
};
