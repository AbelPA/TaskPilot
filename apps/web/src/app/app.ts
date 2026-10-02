import { Component } from '@angular/core';
import { RouterLink, RouterOutlet } from '@angular/router';
import { AudioExtractionNotificationCenter } from './features/audio-extraction/notifications/notification-center';

@Component({
  selector: 'app-root',
  imports: [RouterLink, RouterOutlet, AudioExtractionNotificationCenter],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {}
