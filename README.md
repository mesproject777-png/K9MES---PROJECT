# K9

This project was generated using [Angular CLI](https://github.com/angular/angular-cli) version 20.0.2.

## Development server

To start a local development server, run:

```bash
ng serve
```

Once the server is running, open your browser and navigate to `http://localhost:4200/`. The application will automatically reload whenever you modify any of the source files.

## Local PostgreSQL setup

1. Copy `.env.local.example` to `.env.local`.
2. Update `PGPASSWORD` in `.env.local` with your local PostgreSQL `postgres` user password.
3. Ensure the `MESDB` PostgreSQL database exists with the application tables.
4. Run `npm.cmd run start:api` to start the .NET 8 backend API on `http://localhost:5000`.
5. Run `npm.cmd start` to start the Angular app on `http://localhost:4200`.

Tip: You can also run `dev.cmd` from the `K9/` folder to start both the Angular frontend and .NET backend.

## Troubleshooting

### PowerShell: "npm.ps1 cannot be loaded"

If you see a PowerShell error about `npm.ps1` not being digitally signed:

- Use `npm.cmd ...` (for example `npm.cmd start`) OR
- Switch your VS Code terminal profile to **Command Prompt**.

### Angular: "spawn EPERM" during `ng serve`

If `ng serve` fails with `spawn EPERM`, Windows may be blocking binaries inside `node_modules` (Mark-of-the-Web).

- Run `powershell -ExecutionPolicy Bypass -File .\\unblock-node-modules.ps1`, then try `ng serve` again.
- If it still fails and *any* Node script that spawns a process fails, check Windows Security > App & browser control > Exploit protection and ensure `node.exe` is allowed to create child processes.

## Code scaffolding

Angular CLI includes powerful code scaffolding tools. To generate a new component, run:

```bash
ng generate component component-name
```

For a complete list of available schematics (such as `components`, `directives`, or `pipes`), run:

```bash
ng generate --help
```

## Building

To build the project run:

```bash
ng build
```

This will compile your project and store the build artifacts in the `dist/` directory. By default, the production build optimizes your application for performance and speed.

## Running unit tests

To execute unit tests with the [Karma](https://karma-runner.github.io) test runner, use the following command:

```bash
ng test
```

## Running end-to-end tests

For end-to-end (e2e) testing, run:

```bash
ng e2e
```

Angular CLI does not come with an end-to-end testing framework by default. You can choose one that suits your needs.

## Additional Resources

For more information on using the Angular CLI, including detailed command references, visit the [Angular CLI Overview and Command Reference](https://angular.dev/tools/cli) page.
