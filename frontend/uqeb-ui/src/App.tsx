import { BrowserRouter, Routes, Route, Navigate } from 'react-router-dom';
import { AuthProvider } from './context/AuthContext';
import ProtectedRoute from './components/ProtectedRoute';
import Layout from './components/Layout';
import Login from './pages/Login';
import Dashboard from './pages/Dashboard';
import TransactionsList from './pages/TransactionsList';
import TransactionForm from './pages/TransactionForm';
import TransactionDetail from './pages/TransactionDetail';
import Reports from './pages/Reports';
import { UsersPage, DepartmentsPage, ExternalPartiesPage } from './pages/AdminPages';
import LetterTemplatePage from './pages/LetterTemplatePage';
import SecurityPage from './pages/SecurityPage';
import TransactionImport from './pages/TransactionImport';

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route path="/login" element={<Login />} />
          <Route element={<ProtectedRoute><Layout /></ProtectedRoute>}>
            <Route index element={<Dashboard />} />
            <Route path="transactions" element={<TransactionsList />} />
            <Route
              path="transactions/import"
              element={(
                <ProtectedRoute requiredRoles={['Admin']}>
                  <TransactionImport />
                </ProtectedRoute>
              )}
            />
            <Route path="transactions/new" element={<TransactionForm mode="create" />} />
            <Route path="transactions/:id" element={<TransactionDetail />} />
            <Route path="transactions/:id/edit" element={<TransactionForm mode="edit" />} />
            <Route path="reports" element={<Reports />} />
            <Route path="letter-template" element={<LetterTemplatePage />} />
            <Route path="users" element={<UsersPage />} />
            <Route path="departments" element={<DepartmentsPage />} />
            <Route path="external-parties" element={<ExternalPartiesPage />} />
            <Route
              path="security"
              element={(
                <ProtectedRoute requiredRoles={["Admin"]}>
                  <SecurityPage />
                </ProtectedRoute>
              )}
            />
          </Route>
          <Route path="*" element={<Navigate to="/" replace />} />
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
