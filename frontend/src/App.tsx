import { Routes, Route } from "react-router-dom";
import DashboardPage from "./pages/DashboardPage";
import ReceiptsPage from "./pages/ReceiptsPage";
import StaffAccess from "./components/StaffAccess";
import LoyversePage from "./pages/LoyversePage";

function App() {
  return (
    <div className="min-h-screen bg-gray-50 text-gray-900">
      <StaffAccess><Routes>
        <Route path="/" element={<DashboardPage />} />
        <Route path="/receipts" element={<ReceiptsPage />} />
        <Route path="/loyverse" element={<LoyversePage />} />
      </Routes></StaffAccess>
    </div>
  );
}

export default App;
