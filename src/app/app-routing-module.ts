import { NgModule } from '@angular/core';
import { RouterModule, Routes } from '@angular/router';
import { LoginComponent } from './login/login.component';
import { DashboardComponent } from './dashboard/dashboard.component';

import { authGuard } from './guards/auth.guard';
import { permissionGuard } from './guards/permission.guard';


const routes: Routes = [
  { path: 'login', component: LoginComponent },
  {
    path: 'dashboard',
    component: DashboardComponent,
    canActivate: [authGuard],
    children: [
      { path: '', redirectTo: 'home', pathMatch: 'full' },
      { path: 'home', loadChildren: () => import('./dashboard/home/home.module').then((m) => m.HomeModule), canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
      {
        path: 'workflow',
        loadChildren: () => import('./dashboard/workflow/workflow.module').then((m) => m.WorkflowModule),
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/home' }
      },
      {
        path: 'workorder',
        loadChildren: () => import('./dashboard/work-order/work-order.module').then((m) => m.WorkOrderModule),
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/home' }
      },
      { path: 'work-order', redirectTo: 'workorder', pathMatch: 'full' },
      { path: 'work-order/SNList', redirectTo: 'workorder/SNList', pathMatch: 'full' },
      { path: 'sn-result', loadChildren: () => import('./dashboard/sn-result/sn-result.module').then((m) => m.SnResultModule), canActivate: [permissionGuard], data: { pageKey: 'dashboard/home' } },
      {
        path: 'label',
        loadChildren: () => import('./dashboard/label/label.module').then((m) => m.LabelModule),
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/label' }
      },
      {
        path: 'reports',
        loadChildren: () => import('./dashboard/reports/reports.module').then((m) => m.ReportsModule),
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/reports' }
      },
      { path: 'users', loadChildren: () => import('./dashboard/users/users.module').then((m) => m.UsersModule), canActivate: [permissionGuard], data: { pageKey: 'dashboard/users' } },
      { path: 'role', loadChildren: () => import('./dashboard/role/role.module').then((m) => m.RoleModule), canActivate: [permissionGuard], data: { pageKey: 'dashboard/role' } },
      { path: 'profile', loadChildren: () => import('./dashboard/profile/profile.module').then((m) => m.ProfileModule), canActivate: [permissionGuard], data: { pageKey: 'dashboard/profile' } },
      { path: 'myroute', loadChildren: () => import('./dashboard/myroute/myroute.module').then((m) => m.MyrouteModule), canActivate: [permissionGuard], data: { pageKey: 'dashboard/myroute' } },
      {
        path: 'operations',
        loadChildren: () => import('./dashboard/operations/operations.module').then((m) => m.OperationsModule),
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/operations/assembly' }
      },
      {
        path: 'master',
        loadChildren: () => import('./dashboard/master/master.module').then((m) => m.MasterModule),
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/master/menu' }
      },
      {
        path: 'engineering',
        loadChildren: () => import('./dashboard/engineering/engineering.module').then((m) => m.EngineeringModule),
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/engineering/menu' }
      },
      {
        path: 'manager',
        loadChildren: () => import('./dashboard/manager/manager.module').then((m) => m.ManagerModule),
        canActivate: [permissionGuard],
        data: { pageKey: 'dashboard/manager/menu' }
      },

    ]
  },
  { path: '', redirectTo: 'login', pathMatch: 'full' },
];


@NgModule({
  imports: [RouterModule.forRoot(routes)],
  exports: [RouterModule]
})
export class AppRoutingModule { }
