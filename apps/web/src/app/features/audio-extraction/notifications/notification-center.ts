import { Component, inject, signal } from '@angular/core';
import { isPlatformBrowser } from '@angular/common';
import { PLATFORM_ID } from '@angular/core';
import { AudioExtractionNotificationService } from './audio-extraction-notification.service';

@Component({
  selector: 'app-audio-extraction-notification-center',
  templateUrl: './notification-center.html',
  styleUrl: './notification-center.scss',
})
export class AudioExtractionNotificationCenter {
  protected readonly notifications = inject(AudioExtractionNotificationService);
  protected readonly expanded = signal(false);

  constructor() {
    if (isPlatformBrowser(inject(PLATFORM_ID))) {
      void this.notifications.connect();
    }
  }

  protected toggle(): void {
    this.expanded.update((expanded) => !expanded);
  }
}
