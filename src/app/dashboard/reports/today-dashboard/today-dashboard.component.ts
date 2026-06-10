import { Component } from '@angular/core';

import { ReportViewBridge } from '../report-view-bridge';

@Component({
  selector: 'app-today-dashboard',
  standalone: false,
  templateUrl: './today-dashboard.component.html',
})
export class TodayDashboardComponent extends ReportViewBridge {}
