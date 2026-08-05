Create a React app (Vite + TypeScript + Tailwind CSS) called "MyTravels POI Viewer".

Data source: a .NET API at http://localhost:5101 (CORS must be configured
server-side for the Vite dev origin — assume http://localhost:5100).

Endpoints used:
- GET  /api/PointOfInterest
    → PointOfInterestDto[]: { id, pointOfInterestKey, latitude, longitude,
      pointOfInterestTypeId, pointOfInterestType, pointOfInterestStatusId,
      pointOfInterestStatus, primaryColor, secondaryColor, dateCreated,
      formattedAddress, tags: { name }[] }
- GET  /api/PointOfInterest/{id}?resizedImage=true
    → base64-encoded image string, used to render the POI's photo/thumbnail
- POST /api/PointOfInterest/image  (multipart/form-data, field name "image")
    → { id } — uploads a photo; GPS coordinates and address are extracted
      and resolved asynchronously on the backend (EXIF + geocoding), so the
      new POI's coordinates/address may not be ready immediately

Features:
1. Map view (react-leaflet + OpenStreetMap tile layer) showing a pin for
   every POI returned by GET /api/PointOfInterest. Skip/flag POIs whose
   latitude and longitude are both 0 (not yet geocoded) instead of
   plotting them at (0,0).
2. Clicking a pin opens a dialog/modal with that POI's details: resized
   image (fetched from the image-by-id endpoint), formatted address,
   type, status, tags, and date created.
3. An "Upload Image" button/dialog that lets the user pick an image file
   and POSTs it to /api/PointOfInterest/image. Show upload progress and
   a clear error if the image has no usable GPS EXIF data.
4. After a successful upload, refresh the POI list automatically. Since
   geocoding/address resolution happens asynchronously, poll GET
   /api/PointOfInterest every few seconds for a short window (or until
   the new POI's coordinates become non-zero) rather than assuming the
   list is immediately up to date.
5. Minimalistic design: clean neutral palette, generous whitespace,
   simple sans-serif type, no heavy shadows/gradients.

Tech: React + Vite + TypeScript, Tailwind CSS, react-leaflet + leaflet,
fetch or axios for API calls.
