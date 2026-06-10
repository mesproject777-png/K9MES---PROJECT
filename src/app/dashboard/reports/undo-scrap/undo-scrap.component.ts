import { Component } from '@angular/core';

import { ReportViewBridge } from '../report-view-bridge';

@Component({
  selector: 'app-undo-scrap',
  standalone: false,
  templateUrl: './undo-scrap.component.html',
})
export class UndoScrapComponent extends ReportViewBridge {}
