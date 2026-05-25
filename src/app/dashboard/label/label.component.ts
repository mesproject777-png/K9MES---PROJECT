import { Component, ElementRef, ViewChild, AfterViewInit, Renderer2 } from '@angular/core';

@Component({
  selector: 'app-label',
  standalone: false,
  templateUrl: './label.component.html',
  styleUrl: './label.component.scss'
})
export class LabelComponent  implements AfterViewInit {

  @ViewChild('taskContainer', { static: true }) taskContainer!: ElementRef;
  @ViewChild('linesSVG', { static: true }) linesSVG!: ElementRef<SVGElement>;

  totalTasks = 37; // Total tasks
  tasksPerRow = 8; // Tasks per row
  diamondTasks = [3, 8, 14, 17, 22, 26]; // Tasks that are diamond-shaped
  diamondColors: Record<number, string> = {
    3: '#f39c12', // Orange
    8: '#f39c12'  // Purple
  };

  iconImages = {
    person: 'https://cdn-icons-png.flaticon.com/512/1839/1839365.png',
    truck: 'https://cdn-icons-png.flaticon.com/512/10849/10849258.png'
  };

  allNodes: HTMLElement[] = [];

  constructor(private renderer: Renderer2) {}

  ngAfterViewInit(): void {
    this.createAllElements();
    requestAnimationFrame(() => {
      this.drawLines();
      this.highlightTask(16); // Highlight task 16 by default
    });
  }

  getTaskLines(i: number): string[] {
    return [`Task ${i}`, `Line 1`, `Line 2`];
  }

  createAllElements(): void {
    const totalItems = this.totalTasks + 2; // Add two for the start and end icons

    for (let i = 0; i < totalItems; i++) {
      let el: HTMLElement;

      // Create icon elements for start (i === 0) and end (i === totalItems - 1)
      if (i === 0 || i === totalItems - 1) {
        el = this.renderer.createElement('div');
        this.renderer.addClass(el, 'icon');
        const img = this.renderer.createElement('img');
        img.src = i === 0 ? this.iconImages.person : this.iconImages.truck;
        img.alt = i === 0 ? 'Person' : 'Truck';
        this.renderer.appendChild(el, img);
      }
      // Create task elements for the rest
      else {
        const taskNum = i;
        el = this.renderer.createElement('div');
        this.renderer.addClass(el, 'task');
        el.dataset['taskNumber'] = taskNum.toString();

        // Add diamond style to specific tasks
        if (this.diamondTasks.includes(taskNum)) {
          this.renderer.addClass(el, 'diamond');
          this.renderer.setStyle(el, 'borderColor', this.diamondColors[taskNum] || '#e94e77');
          this.renderer.setStyle(el, 'color', this.diamondColors[taskNum] || '#e94e77');
        }

        // Add text lines inside the task
        this.getTaskLines(taskNum).forEach(line => {
          const p = this.renderer.createElement('p');
          p.textContent = line;
          this.renderer.appendChild(el, p);
        });

        // Click event to update progress
        this.renderer.listen(el, 'click', () => {
          this.highlightTask(taskNum);
        });
      }

      // Grid positioning: Calculate row and column based on index
      const row = Math.floor(i / this.tasksPerRow) + 1;
      const indexInRow = i % this.tasksPerRow;
      const isEvenRow = row % 2 === 0;
      const col = isEvenRow ? this.tasksPerRow - indexInRow : indexInRow + 1;

      this.renderer.setStyle(el, 'gridRowStart', row);
      this.renderer.setStyle(el, 'gridColumnStart', col);

      this.renderer.appendChild(this.taskContainer.nativeElement, el);
      this.allNodes.push(el);
    }
  }

  drawLines(): void {
    const containerRect = this.taskContainer.nativeElement.getBoundingClientRect();
    const svg = this.linesSVG.nativeElement;
    svg.setAttribute('width', containerRect.width.toString());
    svg.setAttribute('height', containerRect.height.toString());
    svg.innerHTML = ''; // Clear previous lines

    const getCenter = (el: HTMLElement) => {
      const rect = el.getBoundingClientRect();
      return {
        x: rect.left + rect.width / 2 - containerRect.left,
        y: rect.top + rect.height / 2 - containerRect.top
      };
    };

    for (let i = 0; i < this.allNodes.length - 1; i++) {
      const start = getCenter(this.allNodes[i]);
      const end = getCenter(this.allNodes[i + 1]);

      const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
      line.setAttribute('x1', start.x.toString());
      line.setAttribute('y1', start.y.toString());
      line.setAttribute('x2', end.x.toString());
      line.setAttribute('y2', end.y.toString());
      line.setAttribute('stroke', 'black');
      line.setAttribute('stroke-width', '2');
      line.setAttribute('stroke-dasharray', '1,1');
      svg.appendChild(line);
    }
  }

  highlightTask(taskNumber_: number): void {
    // Remove previous highlights
    this.taskContainer.nativeElement.querySelectorAll('.task.highlighted, .task.completed').forEach((el: HTMLElement) =>
      el.classList.remove('highlighted', 'completed')
    );

    //Highlight the current task and mark previous ones as completed
    this.allNodes.forEach((el, i) => {
      const taskNumber = el.dataset['taskNumber'];
      const num = taskNumber ? parseInt(taskNumber, 10) : NaN;

      if (num < Number(taskNumber_)) {
        el.classList.add('completed');
      } else if (num === Number(taskNumber_)) {
        el.classList.add('highlighted');
      }
    });


    //  const taskElement = this.allNodes[taskNumber_];
    // if (taskElement?.classList.contains('task')) {
    //   taskElement.classList.add('highlighted');
    // }

  }
}
