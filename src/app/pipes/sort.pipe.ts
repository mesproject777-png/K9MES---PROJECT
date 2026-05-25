import { Pipe, PipeTransform } from '@angular/core';

@Pipe({
  name: 'sort',
  standalone: false
})
export class SortPipe implements PipeTransform {
  transform<T>(array: T[], field: string, reverse: boolean = false): T[] {
    if (!Array.isArray(array)) {
      return array;
    }

    const sorted = [...array].sort((a: any, b: any) => {
      const valueA = a[field];
      const valueB = b[field];

      if (valueA === valueB) return 0;
      if (valueA === null || valueA === undefined) return 1;
      if (valueB === null || valueB === undefined) return -1;

      if (typeof valueA === 'string') {
        return valueA.localeCompare(valueB);
      }

      return valueA < valueB ? -1 : 1;
    });

    return reverse ? sorted.reverse() : sorted;
  }
}
