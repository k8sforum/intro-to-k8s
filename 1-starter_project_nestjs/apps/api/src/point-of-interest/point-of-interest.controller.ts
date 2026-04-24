import {
  Controller, Get, Put, Post, Query, Body, UploadedFile,
  UseInterceptors, Inject, HttpCode,
} from '@nestjs/common';
import { FileInterceptor } from '@nestjs/platform-express';
import { ApiTags, ApiConsumes, ApiBody, ApiOperation, ApiResponse, ApiQuery } from '@nestjs/swagger';
import {
  IPointOfInterestService, UpdatePointOfInterestStatusDto,
  SaveEntityResponseDto, PointOfInterestDto, TagDto,
  GetPointOfInterestResponse, RequiredParameterNotFoundException,
  ApiErrorDto,
} from '@mytravels/contract';

@ApiTags('point-of-interest')
@ApiResponse({ status: 403, type: ApiErrorDto, description: 'Missing or invalid parameter' })
@ApiResponse({ status: 404, type: ApiErrorDto, description: 'Resource not found' })
@ApiResponse({ status: 500, type: ApiErrorDto, description: 'Internal server error' })
@Controller('api/PointOfInterest')
export class PointOfInterestController {
  constructor(
    @Inject(IPointOfInterestService)
    private readonly service: IPointOfInterestService,
  ) {}

  @Get()
  @ApiOperation({ summary: 'Get all points of interest' })
  @ApiResponse({ status: 200, type: [PointOfInterestDto], description: 'List of all points of interest' })
  async getAll(): Promise<PointOfInterestDto[]> {
    const responses = await this.service.getAll();
    return this.toDto(responses);
  }

  @Get('filter')
  @ApiOperation({ summary: 'Get points of interest by tag' })
  @ApiQuery({ name: 'filterString', description: 'Tag name to filter by', example: 'beach' })
  @ApiResponse({ status: 200, type: [PointOfInterestDto], description: 'Filtered list of points of interest' })
  async getByTag(@Query('filterString') filterString: string): Promise<PointOfInterestDto[]> {
    const responses = await this.service.getByTag(filterString);
    return this.toDto(responses);
  }

  @Put('status')
  @ApiOperation({ summary: 'Update the status of a point of interest' })
  @ApiResponse({ status: 200, type: SaveEntityResponseDto, description: 'ID of the updated record' })
  async updateStatus(@Body() dto: UpdatePointOfInterestStatusDto): Promise<SaveEntityResponseDto> {
    const id = await this.service.updateStatus(dto.pointOfInterestKey, dto.statusId);
    return { id, key: null, status: null };
  }

  @Put()
  @ApiOperation({ summary: 'Replace the image of a point of interest' })
  @ApiQuery({ name: 'pointOfInterestKey', description: 'Unique key of the point of interest', example: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890' })
  @ApiConsumes('multipart/form-data')
  @ApiBody({ schema: { type: 'object', properties: { image: { type: 'string', format: 'binary' } } } })
  @ApiResponse({ status: 200, type: SaveEntityResponseDto, description: 'ID of the updated record' })
  @UseInterceptors(FileInterceptor('image'))
  async updateImage(
    @UploadedFile() image: Express.Multer.File,
    @Query('pointOfInterestKey') pointOfInterestKey: string,
  ): Promise<SaveEntityResponseDto> {
    if (!image) throw new RequiredParameterNotFoundException('image');
    if (!pointOfInterestKey) throw new RequiredParameterNotFoundException('pointOfInterestKey');
    const id = await this.service.updatePointOfInterest(image, pointOfInterestKey);
    return { id, key: null, status: null };
  }

  @Post('image')
  @HttpCode(200)
  @ApiOperation({ summary: 'Upload an image to create a new point of interest' })
  @ApiConsumes('multipart/form-data')
  @ApiBody({ schema: { type: 'object', properties: { image: { type: 'string', format: 'binary' } } } })
  @ApiResponse({ status: 200, type: SaveEntityResponseDto, description: 'ID of the created record' })
  @UseInterceptors(FileInterceptor('image'))
  async uploadImage(@UploadedFile() image: Express.Multer.File): Promise<SaveEntityResponseDto> {
    if (!image) throw new RequiredParameterNotFoundException('image');
    const id = await this.service.saveFileAsPointOfInterest(image);
    return { id, key: null, status: null };
  }

  private toDto(responses: GetPointOfInterestResponse[]): PointOfInterestDto[] {
    const grouped = new Map<number, GetPointOfInterestResponse[]>();
    for (const r of responses) {
      if (!grouped.has(r.pointOfInterestId)) grouped.set(r.pointOfInterestId, []);
      grouped.get(r.pointOfInterestId).push(r);
    }

    return Array.from(grouped.values()).map((group) => {
      const first = group[0];
      const dto = new PointOfInterestDto();
      dto.id = first.pointOfInterestId;
      dto.dateCreated = first.dateCreated;
      dto.formattedAddress = first.formattedAddress;
      dto.latitude = first.latitude;
      dto.longitude = first.longitude;
      dto.pointOfInterestType = first.pointOfInterestType;
      dto.pointOfInterestTypeId = first.pointOfInterestTypeId;
      dto.pointOfInterestKey = first.pointOfInterestKey;
      dto.pointOfInterestStatusId = first.pointOfInterestStatusId;
      dto.pointOfInterestStatus = first.pointOfInterestStatus;
      dto.primaryColor = first.primaryColor;
      dto.secondaryColor = first.secondaryColor;
      dto.tags = group
        .filter((r) => r.tagId != null)
        .map((r) => {
          const tag = new TagDto();
          tag.id = r.tagId;
          tag.name = r.tagName;
          tag.dateCreated = r.tagDateCreated;
          return tag;
        });
      return dto;
    });
  }
}
