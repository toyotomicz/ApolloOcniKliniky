import type { ReactNode } from 'react'
import { createBrowserRouter, RouterProvider, type RouteObject } from 'react-router'
import { AdminSessionProvider, ClinicSessionProvider, RequireAdmin, RequireClinic } from './auth/sessions'
import { ToastProvider } from './components/Toast'
import { PortalLayout } from './layouts/PortalLayout'
import { PublicLayout } from './layouts/PublicLayout'
import { ClinicLoginPage } from './pages/ClinicLoginPage'
import { ClinicPortalPage } from './pages/ClinicPortalPage'
import { ClinicProfileEditPage } from './pages/ClinicProfileEditPage'
import { loadChunk } from './lib/loadChunk'
import { NotFoundPage, PrivacyPage, RouteErrorPage } from './pages/InfoPages'
import { LandingPage } from './pages/LandingPage'
import { RegistrationPage } from './pages/RegistrationPage'

export const routes: RouteObject[] = [
  {
    // One error boundary for every route (failed lazy loads, unexpected render errors).
    errorElement: <RouteErrorPage />,
    children: [
      {
        element: <PublicLayout />,
        children: [
          { index: true, element: <LandingPage /> },
          { path: 'registrace', element: <RegistrationPage /> },
          { path: 'prihlaseni', element: <ClinicLoginPage /> },
          { path: 'ochrana-osobnich-udaju', element: <PrivacyPage /> },
          { path: '*', element: <NotFoundPage /> },
        ],
      },
      {
        path: 'portal',
        element: <RequireClinic />,
        children: [
          {
            element: <PortalLayout />,
            children: [
              { index: true, element: <ClinicPortalPage /> },
              { path: 'udaje', element: <ClinicProfileEditPage /> },
            ],
          },
        ],
      },
      // The administration is loaded on demand, so public visitors never download it.
      { path: 'admin/prihlaseni', lazy: async () => ({ Component: (await loadChunk(() => import('./pages/admin/AdminLoginPage'))).AdminLoginPage }) },
      {
        path: 'admin',
        element: <RequireAdmin />,
        children: [
          {
            lazy: async () => ({ Component: (await loadChunk(() => import('./layouts/AdminLayout'))).AdminLayout }),
            children: [
              { index: true, lazy: async () => ({ Component: (await loadChunk(() => import('./pages/admin/AdminDashboardPage'))).AdminDashboardPage }) },
              { path: 'kliniky', lazy: async () => ({ Component: (await loadChunk(() => import('./pages/admin/AdminClinicsPage'))).AdminClinicsPage }) },
              {
                path: 'kliniky/:id',
                lazy: async () => ({ Component: (await loadChunk(() => import('./pages/admin/AdminClinicDetailPage'))).AdminClinicDetailPage }),
              },
            ],
          },
        ],
      },
    ],
  },
]

export function AppProviders({ children }: { children: ReactNode }) {
  return (
    <ToastProvider>
      <ClinicSessionProvider>
        <AdminSessionProvider>{children}</AdminSessionProvider>
      </ClinicSessionProvider>
    </ToastProvider>
  )
}

const router = createBrowserRouter(routes)

export default function App() {
  return (
    <AppProviders>
      <RouterProvider router={router} />
    </AppProviders>
  )
}
