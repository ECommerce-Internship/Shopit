function App() {
  return (
    <div className="min-h-screen flex items-center justify-center bg-gray-100">
      <div className="text-center p-8 bg-white rounded-lg shadow-md">
        <h1 className="text-3xl font-bold text-purple-600 mb-2">
          Shopit UI Scaffold
        </h1>
        <p className="text-gray-600">
          API URL: <code className="text-sm bg-gray-100 px-2 py-1 rounded">{import.meta.env.VITE_API_URL}</code>
        </p>
      </div>
    </div>
  )
}

export default App