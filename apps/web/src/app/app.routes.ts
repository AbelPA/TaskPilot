import { Routes } from '@angular/router';
export const routes: Routes = [
  {
    path: 'extract-audio',
    loadComponent: () =>
      import('./features/audio-extraction/audio-extraction-page').then(module => module.AudioExtractionPage),
  },
  {
    path: 'grammar',
    loadComponent: () =>
      import('./features/grammar/topics-page').then(module => module.TopicsPage),
  },
  {
    path: 'grammar/:slug/practice',
    loadComponent: () =>
      import('./features/grammar/practice-page').then(module => module.PracticePage),
  },
  {
    path: 'grammar/:slug',
    loadComponent: () =>
      import('./features/grammar/theory-page').then(module => module.TheoryPage),
  },
  {
    path: 'settings',
    loadComponent: () =>
      import('./features/settings/settings-page').then(module => module.SettingsPage),
  },
  { path: '', pathMatch: 'full', redirectTo: 'extract-audio' },
  { path: '**', redirectTo: 'extract-audio' },
];
