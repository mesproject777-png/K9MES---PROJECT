import { NgModule, provideBrowserGlobalErrorListeners } from '@angular/core';
import { BrowserModule } from '@angular/platform-browser';
import { HttpClientModule } from '@angular/common/http';
import { ReactiveFormsModule, FormsModule } from '@angular/forms';

import { AppRoutingModule } from './app-routing-module';
import { App } from './app';
import { LoginComponent } from './login/login.component';
import { DashboardComponent } from './dashboard/dashboard.component';
import { SidebarComponent } from './sidebar/sidebar.component';
import { HomeComponent } from './dashboard/home/home.component';
import { UsersComponent } from './dashboard/users/users.component';
import { RoleComponent } from './dashboard/role/role.component';
import { ProfileComponent } from './dashboard/profile/profile.component';
import { MasterComponent } from './dashboard/master/master.component';
import { MyrouteComponent } from './dashboard/myroute/myroute.component';
import { MasterstationComponent } from './dashboard/master/masterstation/masterstation.component';
import { MasterusersComponent } from './dashboard/master/masterusers/masterusers.component';
import { MastermenuComponent } from './dashboard/master/mastermenu/mastermenu.component';
import { MasterroutingComponent } from './dashboard/master/masterrouting/masterrouting.component';
import { MasterproductlineComponent } from './dashboard/master/masterproductline/masterproductline.component';
import { MastersitesComponent } from './dashboard/master/mastersites/mastersites.component';
import { EngineeringComponent } from './dashboard/engineering/engineering.component';
import { EngineeringmenuComponent } from './dashboard/engineering/engineeringmenu/engineeringmenu.component';
import { PntypeComponent } from './dashboard/engineering/pntype/pntype.component';
import { SnTypeComponent } from './dashboard/engineering/sntype/sntype.component';
import { PartnumberComponent } from './dashboard/engineering/partnumber/partnumber.component';
import { ItemrevisionsComponent } from './dashboard/engineering/itemrevisions/itemrevisions.component';
import { RoutingComponent } from './dashboard/engineering/routing/routing.component';
import { StationsComponent } from './dashboard/engineering/stations/stations.component';
import { EpvuploadComponent } from './dashboard/engineering/epvupload/epvupload.component';
import { AssemblydefinitionComponent } from './dashboard/engineering/assemblydefinition/assemblydefinition.component';
import { FivestepwizardComponent } from './dashboard/engineering/fivestepwizard/fivestepwizard.component';
import { ManagerComponent } from './dashboard/manager/manager.component';
import { ManagermenuComponent } from './dashboard/manager/managermenu/managermenu.component';
import { WorkordersComponent } from './dashboard/manager/workorders/workorders.component';
import { SgdpoComponent } from './dashboard/manager/sgdpo/sgdpo.component';
import { BomComponent } from './dashboard/bom/bom.component';
import { EcnComponent } from './dashboard/ecn/ecn.component';
import { LabelComponent } from './dashboard/label/label.component';
import { ReportsComponent } from './dashboard/reports/reports.component';
import { SortPipe } from './pipes/sort.pipe';
import { OperationsAssemblyComponent } from './dashboard/operations/assembly/assembly.component';
import { OperationsmenuComponent } from './dashboard/operations/operationsmenu/operationsmenu.component';
import { OpenPackagesComponent } from './dashboard/operations/packing/open-packages/open-packages.component';
import { ClosedPackagesComponent } from './dashboard/operations/packing/closed-packages/closed-packages.component';
import { ShippedPackagesComponent } from './dashboard/operations/packing/shipped-packages/shipped-packages.component';

@NgModule({
  declarations: [
    App,
    LoginComponent,
    DashboardComponent,
    SidebarComponent,
    HomeComponent,
    UsersComponent,
    RoleComponent,
    ProfileComponent,
    MasterComponent,
    MyrouteComponent,
    MasterstationComponent,
    MasterusersComponent,
    MastermenuComponent,
    MasterroutingComponent,
    MasterproductlineComponent,
    MastersitesComponent,
    EngineeringComponent,
    EngineeringmenuComponent,
    PntypeComponent,
    SnTypeComponent,
    PartnumberComponent,
    ItemrevisionsComponent,
    StationsComponent,
    RoutingComponent,
    EpvuploadComponent,
    AssemblydefinitionComponent,
    FivestepwizardComponent,
    ManagerComponent,
    ManagermenuComponent,
    WorkordersComponent,
    SgdpoComponent,
    BomComponent,
    EcnComponent,
    LabelComponent,
    ReportsComponent,
    OperationsAssemblyComponent,
    OperationsmenuComponent,
    OpenPackagesComponent,
    ClosedPackagesComponent,
    ShippedPackagesComponent,
    SortPipe
  ],
  imports: [
    BrowserModule,
    HttpClientModule,
    ReactiveFormsModule,
    FormsModule,
    AppRoutingModule
  ],
  providers: [
    provideBrowserGlobalErrorListeners()
  ],
  bootstrap: [App]
})
export class AppModule { }
