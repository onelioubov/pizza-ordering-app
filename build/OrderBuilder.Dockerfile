FROM node:22-alpine AS build
WORKDIR /repo
COPY package.json package-lock.json ./
COPY src/Pizza.OrderBuilder/package.json src/Pizza.OrderBuilder/
COPY tests/Pizza.OrderBuilder.Tests/package.json tests/Pizza.OrderBuilder.Tests/
RUN npm ci
COPY src/Pizza.OrderBuilder/ src/Pizza.OrderBuilder/
RUN npm run build -w src/Pizza.OrderBuilder

FROM nginx:1.27-alpine
COPY build/nginx.conf /etc/nginx/conf.d/default.conf
COPY --from=build /repo/src/Pizza.OrderBuilder/dist /usr/share/nginx/html
