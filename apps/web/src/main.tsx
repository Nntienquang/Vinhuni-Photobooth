import React from 'react';
import ReactDOM from 'react-dom/client';
import { BrowserRouter, Navigate, Route, Routes } from 'react-router-dom';
import { Admin } from './pages/Admin';
import { Booth } from './pages/Booth';
import { Photo } from './pages/Photo';
import './style.css';
import './portrait.css';
ReactDOM.createRoot(document.getElementById('root')!).render(<React.StrictMode><BrowserRouter><Routes><Route path="/booth" element={<Booth />} /><Route path="/admin/*" element={<Admin />} /><Route path="/p/:shareId" element={<Photo />} /><Route path="*" element={<Navigate to="/admin/dashboard" replace />} /></Routes></BrowserRouter></React.StrictMode>);
