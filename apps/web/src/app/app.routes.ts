import { Routes } from '@angular/router';
import { AudioExtractionPage } from './features/audio-extraction/audio-extraction-page';

export const routes: Routes = [
  { path: 'extract-audio', component: AudioExtractionPage },
  { path: '', pathMatch: 'full', redirectTo: 'extract-audio' },
  { path: '**', redirectTo: 'extract-audio' },
];
