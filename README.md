
This is a full-stack e-commerce platform built using ASP.NET Core Razor Pages with Firebase. The goal is to help businesses move online and connect their website with their in-store systems.


The platform pulls all product information from the business’s ERP system and displays it on the website. When a customer places an order on the site or through WhatsApp, the order is sent straight to the POS system so staff can process it. This keeps online and in-store operations in sync.
The system supports two main groups of users: ERP users (admins, managers, and staff) and normal online customers.


Key Features
Authentication and Authorisation:
•	Session-based authentication using Firebase
•	Role-based access (Admin, Manager, Staff, Customer)
•	Permission rules for managing orders, reports, and users

E-Commerce Features:
•	Product catalogue that comes directly from the ERP
•	Real-time inventory updates
•	Shopping cart and wishlist
•	Order placement and tracking
•	Orders sent automatically to the POS system
•	Branch-level stock visibility

Admin and Management Tools:
•	Admin dashboard for products, orders, and users
•	Content management for editing website pages
•	POS system integration for online order processing
•	Sales reports

Performance Improvements:
•	Output caching for products and static pages
•	Memory caching for frequently used data

AI Integration (In Progress):
•	Simple customer service chatbot using the Groq API

Technology Stack:
•	ASP.NET Core 8.0 Razor Pages

Database: 
•	Google Cloud Firestore
•	Firebase Auth with custom session handling
•	Firebase Storage


Architecture
The project uses a service-based structure.
Controllers handle API calls, Middleware manages authentication, Models define the data, and Services handle Firebase work, ERP syncing, cart logic, and POS communication.

