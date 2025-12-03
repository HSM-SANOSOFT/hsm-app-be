import { Injectable } from '@nestjs/common';

@Injectable()
export class AvailabilityService {
  getSpecialties() {
    return;
  }

  getProvidersBySpecialty(params: { specialtyId: string }) {
    return { params };
  }

  getProviderDates(
    params: { providerId: string },
    query: {
      specialtyId?: string;
      dateFrom?: string;
      dateTo?: string;
    },
  ) {
    return { params, query };
  }

  getProviderSlots(
    params: { providerId: string; date: string },
    query: { specialtyId?: string },
  ) {
    return { params, query };
  }
}
