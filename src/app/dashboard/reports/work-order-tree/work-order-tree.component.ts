import {
  AfterViewChecked,
  AfterViewInit,
  Component,
  ElementRef,
  HostListener,
  QueryList,
  ViewChild,
  ViewChildren,
} from '@angular/core';

import { ReportViewBridge } from '../report-view-bridge';

@Component({
  selector: 'app-work-order-tree',
  standalone: false,
  templateUrl: './work-order-tree.component.html',
})
export class WorkOrderTreeComponent extends ReportViewBridge implements AfterViewInit, AfterViewChecked {
  connectorPath = '';
  connectorWidth = 0;
  connectorHeight = 0;
  flowCardsPerRow = this.getFlowCardsPerRow();

  private connectorFrame: number | null = null;
  private lastConnectorSignature = '';

  @ViewChild('reportProcessFlow') private reportProcessFlowRef?: ElementRef<HTMLElement>;
  @ViewChildren('reportFlowNode') private reportFlowNodeRefs?: QueryList<ElementRef<HTMLElement>>;

  ngAfterViewInit(): void {
    this.reportFlowNodeRefs?.changes.subscribe(() => this.queueConnectorRefresh());
    this.queueConnectorRefresh();
  }

  ngAfterViewChecked(): void {
    const signature = this.buildConnectorSignature();
    if (signature !== this.lastConnectorSignature) {
      this.lastConnectorSignature = signature;
      this.queueConnectorRefresh();
    }
  }

  @HostListener('window:resize')
  onWindowResize(): void {
    this.flowCardsPerRow = this.getFlowCardsPerRow();
    this.queueConnectorRefresh();
  }

  private getFlowCardsPerRow(): number {
    const width = Math.max(360, window.innerWidth - 300);
    const availableWidth = Math.max(320, width - 68);
    const estimatedCardWidth = width >= 1240 ? 144 : 156;
    const estimatedLineWidth = width >= 1240 ? 30 : 36;
    const estimatedCards = Math.floor(
      (availableWidth + estimatedLineWidth) / (estimatedCardWidth + estimatedLineWidth)
    );

    return Math.max(2, Math.min(8, estimatedCards));
  }

  private buildConnectorSignature(): string {
    const ids = this.flowNodes.map((node: any) => node.id).join('|');
    const counts = (this.report?.stations || []).map((station: any) => `${station.id}:${station.sn_count}`).join('|');
    return `${this.flowCardsPerRow}:${ids}:${counts}`;
  }

  private queueConnectorRefresh(): void {
    if (typeof window === 'undefined') {
      return;
    }

    if (this.connectorFrame !== null) {
      window.cancelAnimationFrame(this.connectorFrame);
    }

    this.connectorFrame = window.requestAnimationFrame(() => {
      this.connectorFrame = null;
      this.updateConnectorPath();
    });
  }

  private updateConnectorPath(): void {
    const container = this.reportProcessFlowRef?.nativeElement;
    const nodeRefs = this.reportFlowNodeRefs?.toArray() || [];

    if (!container || nodeRefs.length < 2) {
      this.setConnector('', 0, 0);
      return;
    }

    const containerRect = container.getBoundingClientRect();
    const nodeRectsById = new Map<string, DOMRect>();

    nodeRefs.forEach((nodeRef) => {
      const flowId = nodeRef.nativeElement.dataset['flowId'];
      if (flowId) {
        nodeRectsById.set(flowId, nodeRef.nativeElement.getBoundingClientRect());
      }
    });

    const orderedRects = this.flowNodes
      .map((node: any) => nodeRectsById.get(node.id))
      .filter((rect: DOMRect | undefined): rect is DOMRect => Boolean(rect));

    if (orderedRects.length < 2) {
      this.setConnector('', 0, 0);
      return;
    }

    const pathSegments: string[] = [];
    for (let index = 0; index < orderedRects.length - 1; index += 1) {
      const currentRect = orderedRects[index];
      const nextRect = orderedRects[index + 1];
      const currentCenter = this.getRelativeCenter(currentRect, containerRect);
      const nextCenter = this.getRelativeCenter(nextRect, containerRect);
      const sameRow = Math.abs(currentCenter.y - nextCenter.y) < 28;

      pathSegments.push(sameRow
        ? this.buildSameRowConnector(currentRect, nextRect, containerRect)
        : this.buildRowTurnConnector(currentRect, nextRect, containerRect));
    }

    this.setConnector(pathSegments.join(' '), containerRect.width, containerRect.height);
  }

  private buildSameRowConnector(currentRect: DOMRect, nextRect: DOMRect, containerRect: DOMRect): string {
    const currentCenter = this.getRelativeCenter(currentRect, containerRect);
    const nextCenter = this.getRelativeCenter(nextRect, containerRect);
    const flowsRight = nextCenter.x >= currentCenter.x;
    const startX = flowsRight ? currentRect.right - containerRect.left : currentRect.left - containerRect.left;
    const endX = flowsRight ? nextRect.left - containerRect.left : nextRect.right - containerRect.left;
    const y = (currentCenter.y + nextCenter.y) / 2;

    return `M ${startX} ${y} L ${endX} ${y}`;
  }

  private buildRowTurnConnector(currentRect: DOMRect, nextRect: DOMRect, containerRect: DOMRect): string {
    const currentCenter = this.getRelativeCenter(currentRect, containerRect);
    const nextCenter = this.getRelativeCenter(nextRect, containerRect);
    const flowsDown = nextCenter.y >= currentCenter.y;
    const startX = currentCenter.x;
    const startY = flowsDown ? currentRect.bottom - containerRect.top : currentRect.top - containerRect.top;
    const endX = nextCenter.x;
    const endY = flowsDown ? nextRect.top - containerRect.top : nextRect.bottom - containerRect.top;
    const midY = startY + ((endY - startY) / 2);

    if (Math.abs(startX - endX) < 4) {
      return `M ${startX} ${startY} L ${endX} ${endY}`;
    }

    return `M ${startX} ${startY} L ${startX} ${midY} L ${endX} ${midY} L ${endX} ${endY}`;
  }

  private getRelativeCenter(rect: DOMRect, containerRect: DOMRect): { x: number; y: number } {
    return {
      x: rect.left - containerRect.left + (rect.width / 2),
      y: rect.top - containerRect.top + (rect.height / 2),
    };
  }

  private setConnector(path: string, width: number, height: number): void {
    this.connectorPath = path;
    this.connectorWidth = width;
    this.connectorHeight = height;
  }
}
