import { Component } from '@angular/core';

import { ReportViewBridge } from '../report-view-bridge';

@Component({
  selector: 'app-reports-sn-chart',
  standalone: false,
  templateUrl: './sn-chart.component.html',
})
export class SnChartComponent extends ReportViewBridge {}
